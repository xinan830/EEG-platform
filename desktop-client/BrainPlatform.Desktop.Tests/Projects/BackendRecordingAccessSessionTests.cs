using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class BackendRecordingAccessSessionTests
{
    [Fact]
    public async Task ReadsVSampleWindowsInSourceChannelOrderAndPreservesGaps()
    {
        var client = new FakeClient((recordingId, start, end, _) => Task.FromResult(new BackendRawSampleRange(
            recordingId, start, end, 2, ["F3", "C3"], ["V", "V"],
            Enumerable.Range((int)start, (int)(end - start)).Select(sample => (IReadOnlyList<double?>)[sample / 1e6, sample / 2e6]).ToArray(),
            start == 0 ? [new BackendUnavailableInterval(1, 2, "source_gap", ["F3", "C3"])] : [])));
        var session = new BackendRecordingAccessSession(client, Recording(), Project());

        var read = await session.ReadSamplesAsync(new(0, 4), [1, 0], CancellationToken.None);

        Assert.Equal(["C3", "F3"], read.Channels.Select(channel => channel.Label));
        Assert.Equal(2, read.Window.Segments.Count);
        Assert.Equal(0, read.Window.Segments[0].FirstSampleCounter);
        Assert.Equal(2, read.Window.Segments[1].FirstSampleCounter);
        Assert.Equal([0d, 0d], read.Window.Segments[0].SampleMajorValues);
        Assert.Equal(1e-6, read.Window.Segments[1].SampleMajorValues[0]);
        Assert.Equal(2, read.Window.ActualEndSeconds);
        Assert.Equal(new string('b', 64), session.Metadata.Revision);
    }

    [Fact]
    public async Task RejectsMalformedBlocksInsteadOfReorderingOrTruncatingSamples()
    {
        var client = new FakeClient((recordingId, start, end, _) => Task.FromResult(new BackendRawSampleRange(
            recordingId, start, end, 512, ["C3", "F3"], ["V", "V"], [[1d, 2d]], [])));
        var session = new BackendRecordingAccessSession(client, Recording(), Project());

        var error = await Assert.ThrowsAsync<RecordingReadException>(() => session.ReadSamplesAsync(
            new(0, 2), null, CancellationToken.None));

        Assert.Equal("RAW_SAMPLE_CONTRACT_INVALID", error.Code);
    }

    [Fact]
    public void RejectsRatesThatWouldBeRoundedOnTheReviewTimeline()
    {
        var recording = Recording() with { SamplingRateHz = 512.5, DurationSeconds = 10 };

        var error = Assert.Throws<RecordingReadException>(() =>
            new BackendRecordingAccessSession(new FakeClient((_, _, _, _) => throw new NotSupportedException()), recording, Project()));

        Assert.Equal("RECORDING_METADATA_INVALID", error.Code);
    }

    private static BackendRecording Recording() => new("0123456789abcdef0123456789abcdef", "test.edf", ".edf",
        "2026-11-24T00:00:00Z", 2, 2, ["F3", "C3"], ["eeg", "eeg"], ["V", "V"], new string('b', 64), 1024);

    private static BackendResearchProject Project() => new("project-1", "Study", "", "", "");

    private sealed class FakeClient(Func<string, long, long, CancellationToken, Task<BackendRawSampleRange>> read) : IResearchProjectClient
    {
        public Task<BackendResearchProject> EnsureProjectAsync(string projectId, string name, string description, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSubject> CreateSubjectAsync(string projectId, string localCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSession> CreateSessionAsync(string projectId, CreateResearchSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId, string? newSubjectLocalCode, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendRecording> GetRecordingAsync(string recordingId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendRawSampleRange> ReadRawSamplesAsync(string recordingId, long startSample, long endSample, CancellationToken cancellationToken) => read(recordingId, startSample, endSample, cancellationToken);
    }
}

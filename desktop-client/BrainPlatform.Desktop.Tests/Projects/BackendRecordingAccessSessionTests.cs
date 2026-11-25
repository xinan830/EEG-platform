using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Review.Montages;

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

    [Fact]
    public void RestoresImportedSourceColumnsToTheirBoundNativeChannelIndexes()
    {
        var profile = new ChannelConfigurationProfile("p", "p", "", ChannelConfigurationSource.User, "device",
            [new(8, AcquisitionChannelKind.Reference, "F3", true, 0),
             new(3, AcquisitionChannelKind.Bipolar, "C3", false, 1)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var recording = Recording() with
        {
            ImportChannelConfigurationSnapshotJson = JsonSerializer.Serialize(profile),
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":8},{\"source_column\":1,\"native_channel_index\":3}]",
        };

        var session = new BackendRecordingAccessSession(new FakeClient((_, _, _, _) => throw new NotSupportedException()), recording, Project());

        Assert.Equal([8, 3], session.Manifest.Channels.Select(channel => channel.NativeChannelIndex));
        Assert.Equal([AcquisitionChannelKind.Reference, AcquisitionChannelKind.Bipolar],
            session.Manifest.Channels.Select(channel => channel.Kind));
        Assert.Equal(recording.ImportChannelConfigurationSnapshotJson,
            session.Manifest.HardwareConfiguration["channel_configuration_snapshot_json"]);
    }

    [Fact]
    public void PreservesImportedMontageSnapshotsForReviewAndAlgorithmSelection()
    {
        var channelSnapshot = new ChannelConfigurationProfile("p", "p", "", ChannelConfigurationSource.User, "device",
            [new(8, AcquisitionChannelKind.Reference, "F3", true, 0)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var montage = new MontageProfile("saved", "Saved", "", MontageProfileSource.User, "device", "fingerprint",
            channelSnapshot, [new("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0)],
            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "montage-fingerprint");
        var recording = Recording() with
        {
            Channels = ["F3"], ChannelTypes = ["eeg"], ChannelUnits = ["V"],
            ImportChannelConfigurationSnapshotJson = JsonSerializer.Serialize(channelSnapshot),
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":8}]",
            ImportAcquisitionMontageSnapshotJson = JsonSerializer.Serialize(new { Id = "saved" }),
            ImportMontageConfigurationSnapshotJson = JsonSerializer.Serialize(montage),
            ImportMontageCatalogueSnapshotJson = JsonSerializer.Serialize(new[] { montage }),
        };

        var externalProfile = new MontageProfile("workspace-other", "系统其他导联", "", MontageProfileSource.User,
            "device", "fingerprint", channelSnapshot,
            [new("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0)],
            1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "other-fingerprint");
        var session = new BackendRecordingAccessSession(
            new FakeClient((_, _, _, _) => throw new NotSupportedException()), recording, Project());
        var catalog = RecordingMontageCatalog.Build(session.Manifest, [externalProfile]);

        Assert.Equal(recording.ImportChannelConfigurationSnapshotJson,
            session.Manifest.HardwareConfiguration["channel_configuration_snapshot_json"]);
        Assert.Equal(recording.ImportMontageConfigurationSnapshotJson,
            session.Manifest.HardwareConfiguration["montage_configuration_snapshot_json"]);
        Assert.Equal("saved", catalog.AcquisitionMontage?.Id);
        Assert.Equal(["saved"], catalog.CompatibleViewingMontages.Select(item => item.Profile.Id));
    }

    [Fact]
    public void BuildsAlgorithmDescriptorFromBackendIdentityAndResolvedInputs()
    {
        var montage = new HistoricalMontageDescriptor("saved", "r1", "fingerprint", "Saved", true,
            true, null, [new("Fz-REF", "Fz", "eeg", "V", true, null)]);
        var recording = Recording() with
        {
            AnalysisChannels = ["Fz-REF"],
            MontageCatalogue = [montage],
        };

        var descriptor = recording.ToAnalysisDescriptor();

        Assert.Equal(recording.Id, descriptor.Id);
        Assert.Equal(recording.SourceSha256, descriptor.SourceSha256);
        Assert.Equal(["Fz-REF"], descriptor.Channels);
        Assert.Same(montage, Assert.Single(descriptor.MontageCatalogue!));
    }

    [Fact]
    public async Task UsesMappedDisplayLabelsWhileValidatingRawSourceLabelsAndOrder()
    {
        var profile = new ChannelConfigurationProfile("p", "p", "", ChannelConfigurationSource.User, "device",
            [new(8, AcquisitionChannelKind.Reference, "Fz", true, 0),
             new(3, AcquisitionChannelKind.Bipolar, "Pz", true, 1)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var recording = Recording() with
        {
            Channels = ["CH0001", "CH0002"],
            ImportChannelConfigurationSnapshotJson = JsonSerializer.Serialize(profile),
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":8},{\"source_column\":1,\"native_channel_index\":3}]",
        };
        var client = new FakeClient((recordingId, start, end, _) => Task.FromResult(new BackendRawSampleRange(
            recordingId, start, end, 2, ["CH0001", "CH0002"], ["V", "V"], [[1e-6, 2e-6]], [])));
        var session = new BackendRecordingAccessSession(client, recording, Project());

        var read = await session.ReadSamplesAsync(new(0, 1), null, CancellationToken.None);

        Assert.Equal(["Fz", "Pz"], read.Channels.Select(channel => channel.Label));
        Assert.Equal(["Fz", "Pz"], session.Manifest.Channels.Select(channel => channel.Label));
        Assert.Equal([1e-6, 2e-6], read.Window.Segments.Single().SampleMajorValues);
    }

    [Fact]
    public void FallsBackToSourceChannelNameWhenDisabledChannelHasEmptyElectrodeLabel()
    {
        var profile = new ChannelConfigurationProfile("p", "p", "", ChannelConfigurationSource.User, "device",
            [new(4, AcquisitionChannelKind.Reference, "Fz", true, 0),
             new(7, AcquisitionChannelKind.Bipolar, "", false, 1)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var recording = Recording() with
        {
            Channels = ["Fz", "CH0002"],
            ImportChannelConfigurationSnapshotJson = JsonSerializer.Serialize(profile),
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":4},{\"source_column\":1,\"native_channel_index\":7}]",
        };

        var session = new BackendRecordingAccessSession(
            new FakeClient((_, _, _, _) => throw new NotSupportedException()), recording, Project());
        var projected = RecordingMontageCatalog.ProjectRawSignalChannels(session.Manifest);

        Assert.Equal(["Fz", "CH0002"], session.Manifest.Channels.Select(channel => channel.Label));
        Assert.Equal(["Fz"], projected.Channels.Select(channel => channel.Label));
    }

    [Fact]
    public void RejectsCorruptOrMismatchedImportBindingInsteadOfFallingBackToAllChannels()
    {
        var halfPresent = Recording() with
        {
            ImportChannelConfigurationSnapshotJson = "{\"Channels\":[]}",
            ImportChannelColumnMappingJson = null,
        };
        var halfError = Assert.Throws<RecordingReadException>(() => new BackendRecordingAccessSession(
            new FakeClient((_, _, _, _) => throw new NotSupportedException()), halfPresent, Project()));
        Assert.Equal("IMPORT_CHANNEL_BINDING_INVALID", halfError.Code);

        var mismatched = Recording() with
        {
            ImportChannelConfigurationSnapshotJson = JsonSerializer.Serialize(new ChannelConfigurationProfile(
                "p", "p", "", ChannelConfigurationSource.User, "device",
                [new(8, AcquisitionChannelKind.Reference, "Fz", true, 0)],
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)),
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":8}]",
        };
        var mismatchError = Assert.Throws<RecordingReadException>(() => new BackendRecordingAccessSession(
            new FakeClient((_, _, _, _) => throw new NotSupportedException()), mismatched, Project()));
        Assert.Equal("IMPORT_CHANNEL_BINDING_INVALID", mismatchError.Code);

        var malformed = Recording() with
        {
            ImportChannelConfigurationSnapshotJson = "{not-json",
            ImportChannelColumnMappingJson = "[{\"source_column\":0,\"native_channel_index\":0}]",
        };
        var parseError = Assert.Throws<RecordingReadException>(() => new BackendRecordingAccessSession(
            new FakeClient((_, _, _, _) => throw new NotSupportedException()), malformed, Project()));
        Assert.Equal("IMPORT_CHANNEL_BINDING_INVALID", parseError.Code);
    }

    private static BackendRecording Recording() => new("0123456789abcdef0123456789abcdef", "test.edf", ".edf",
        "2026-11-24T00:00:00Z", 2, 2, ["F3", "C3"], ["eeg", "eeg"], ["V", "V"], new string('b', 64), 1024);

    private static BackendResearchProject Project() => new("project-1", "Study", "", "", "");

    private sealed class FakeClient(Func<string, long, long, CancellationToken, Task<BackendRawSampleRange>> read) : IResearchProjectClient
    {
        public Task<SubjectResolutionResponse> ResolveSubjectAsync(string projectId, CreateSubjectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSubject> UpdateSubjectAsync(string projectId, string subjectId, CreateSubjectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubjectDeletionResponse> DeleteSubjectAsync(string projectId, string subjectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSession> BindSessionSubjectAsync(string projectId, string sessionId, BindResearchSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchProject> EnsureProjectAsync(string projectId, string name, string description, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSubject> CreateSubjectAsync(string projectId, string localCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSession> CreateSessionAsync(string projectId, CreateResearchSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSession> RenameSessionAsync(string projectId, string sessionId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendResearchSession> EditSessionAsync(string projectId, string sessionId, EditResearchSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendProjectRecordingDeletion> DeleteImportedRecordingAsync(string projectId, string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId, string? newSubjectLocalCode, string? channelConfigurationSnapshotJson, IProgress<long>? progress, CancellationToken cancellationToken, string? newSubjectName = null, string? newSubjectSex = null, int? ageAtRecordingYears = null) => throw new NotSupportedException();
        public Task<BackendRecording> GetRecordingAsync(string recordingId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackendRawSampleRange> ReadRawSamplesAsync(string recordingId, long startSample, long endSample, CancellationToken cancellationToken) => read(recordingId, startSample, endSample, cancellationToken);
    }
}

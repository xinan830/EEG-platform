using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkRecordingAccessBoundaryTests
{
    [Fact]
    public void RejectsNonVoltSourceBeforeRegistration()
    {
        var manifest = new LocalRawRecordingManifest(
            Guid.NewGuid(), "test", "mV", "device", "device", 100, 1,
            DateTimeOffset.UtcNow,
            [new AcquisitionChannel(0, 0, "Fz", AcquisitionChannelKind.Reference, "mV")],
            new AcquisitionProjectContext("p", "P", "Project", ".", "{}"),
            new Dictionary<string, string>());
        var source = new StubRecordingAccessSession(manifest);

        var exception = Assert.Throws<RecordingAccessException>(() =>
            AlgorithmListViewModel.ValidateSharedRecordingSource(source));

        Assert.Equal(RecordingAccessErrorCodes.SourceUnavailable, exception.Code);
    }

    private sealed class StubRecordingAccessSession(LocalRawRecordingManifest manifest) : IRecordingAccessSession
    {
        public LocalRawRecordingManifest Manifest { get; } = manifest;
        public double DurationSeconds => 1;
        public long FirstSampleCounter => 0;
        public long LastSampleCounter => 99;
        public RecordingAccessMetadata Metadata { get; } = new(
            manifest.SessionId, "test-revision", manifest.SamplingRateHz, 0, 99, [], manifest);

        public Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range, IReadOnlyList<int>? channelIndexes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

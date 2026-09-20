using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewViewModelTests
{
    [Fact]
    public async Task ViewModelExposesAcquisitionAndCurrentViewingMontageSeparately()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var alternate = Profile("备用导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition), new CompatibleRecordingMontage(alternate)],
            []);
        await using var recording = new LocalRawRecordingForTest(Manifest(configuration));
        await using var viewModel = new RecordingReviewViewModel(recording.Reader, catalog, recordingName: "测试记录", projectName: "测试项目");

        await viewModel.InitializeAsync();

        Assert.Equal("采集导联", viewModel.AcquisitionMontageText);
        Assert.Equal("采集导联", viewModel.CurrentViewingMontage?.Name);
        Assert.Equal(1, viewModel.OutputChannelCount);
        Assert.Equal(500, viewModel.SamplingRateHz);
        Assert.Equal(1, viewModel.SourceChannelCount);
        Assert.Equal("测试项目", viewModel.ProjectName);
        Assert.Equal("测试记录", viewModel.RecordingName);
        Assert.Contains(alternate, viewModel.CompatibleViewingMontages);
    }

    [Fact]
    public async Task ViewModelSeekKeepsPlaybackStateSeparateFromMontageState()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        await using var recording = new LocalRawRecordingForTest(Manifest(configuration));
        await using var viewModel = new RecordingReviewViewModel(recording.Reader, catalog);
        await viewModel.InitializeAsync();

        await viewModel.SeekAsync(5);

        Assert.Equal(5, viewModel.PositionSeconds);
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public async Task MissingAcquisitionSnapshotStartsOnExplicitRawSignalView()
    {
        var configuration = Configuration();
        var currentProfile = Profile("当前导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            null,
            AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(currentProfile)],
            []);
        await using var recording = new LocalRawRecordingForTest(Manifest(configuration));
        await using var viewModel = new RecordingReviewViewModel(recording.Reader, catalog);

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.CurrentViewingMontage);
        Assert.Equal(1, viewModel.OutputChannelCount);
        Assert.Contains("未记录", viewModel.AcquisitionMontageText);
    }

    private static LocalRawRecordingManifest Manifest(ChannelConfigurationProfile configuration) => new(
        Guid.NewGuid(),
        "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "V",
        "device",
        "device",
        500,
        1,
        DateTimeOffset.UtcNow,
        [
            new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
            new AcquisitionChannel(1, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
        ],
        new AcquisitionProjectContext("p", "P001", "P", "C:\\p", "{}"),
        new Dictionary<string, string>());

    private static ChannelConfigurationProfile Configuration() => new(
        "channel",
        "通道",
        "",
        ChannelConfigurationSource.User,
        "device",
        [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static MontageProfile Profile(string name, ChannelConfigurationProfile configuration)
    {
        var row = new DerivedMontageChannel("F3-REF", "F3", MontageNegativeKind.OriginalHardwareReference, [], 0);
        var fingerprint = ChannelConfigurationFingerprint.Create(configuration);
        return new MontageProfile(
            name,
            name,
            "",
            MontageProfileSource.User,
            "device",
            fingerprint,
            configuration,
            [row],
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(fingerprint, [row]));
    }

    private sealed class LocalRawRecordingForTest : IAsyncDisposable
    {
        public LocalRawRecordingForTest(LocalRawRecordingManifest manifest)
        {
            Reader = new FakeReader(manifest);
        }

        public FakeReader Reader { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeReader(LocalRawRecordingManifest manifest) : IRecordingReviewReader
    {
        public LocalRawRecordingManifest Manifest { get; } = manifest;

        public double DurationSeconds => 20;

        public Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordingReviewWindow(
                startSeconds,
                startSeconds,
                startSeconds + durationSeconds,
                [new RecordingReviewSegment(0, 1, 2, [1e-6, 0])]));
    }
}

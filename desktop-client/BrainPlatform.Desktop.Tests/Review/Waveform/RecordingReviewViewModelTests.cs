
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

    [Fact]
    public async Task SmoothPlaybackClockDoesNotReloadTheVisibleWindowOnEveryTick()
    {
        var now = 0d;
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration));
        await using var viewModel = new RecordingReviewViewModel(
            reader,
            catalog,
            playbackClockSeconds: () => now);
        await viewModel.InitializeAsync();

        viewModel.TogglePlayback();
        for (var index = 0; index < 100; index++)
        {
            now += 0.01;
            viewModel.TickPlayback();
        }

        // A bounded next block may prefetch, but render-clock ticks must not
        // issue one raw read per tick.
        Assert.InRange(reader.ReadCount, 1, 2);
        Assert.Equal(1, viewModel.PositionSeconds, precision: 6);
    }

    [Fact]
    public async Task ContinuousNavigatorDragCoalescesToTheLatestBoundedRead()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration), durationSeconds: 120);
        await using var viewModel = new RecordingReviewViewModel(reader, catalog);
        await viewModel.InitializeAsync();

        for (var position = 40; position <= 55; position++)
        {
            viewModel.PreviewSeek(position);
        }

        await Task.Delay(180);
        // Throttling begins useful work during the drag, then consumes the
        // newest target at the next cadence; it must not wait for mouse-up.
        Assert.InRange(reader.ReadCount, 2, 3);
        Assert.Equal(55, viewModel.PositionSeconds);
        Assert.True(viewModel.ViewportStartSeconds > 50);
    }

    [Fact]
    public async Task PlaybackCrossingACacheBlockStartsOneForegroundLoadInsteadOfResettingDebounce()
    {
        var now = 0d;
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration), durationSeconds: 120);
        await using var viewModel = new RecordingReviewViewModel(reader, catalog, playbackClockSeconds: () => now);
        await viewModel.InitializeAsync();

        viewModel.TogglePlayback();
        now = 18;
        viewModel.TickPlayback();
        await WaitUntilAsync(() => reader.ReadCount >= 2);

        Assert.InRange(reader.ReadCount, 2, 3);
        Assert.True(viewModel.ViewportStartSeconds > 10);
    }

    [Fact]
    public async Task PaperSpeedDerivesVisibleDurationFromTheViewportWidth()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration));
        await using var viewModel = new RecordingReviewViewModel(reader, catalog);
        await viewModel.InitializeAsync();

        // 960 DIP = 254 mm at WPF's 96 DPI reference. At 30 mm/s this is 8.466... seconds.
        viewModel.UpdateViewportWidth(960);
        await WaitUntilAsync(() => Math.Abs(viewModel.VisibleDurationSeconds - (254d / 30d)) < 0.01);
        Assert.InRange(viewModel.VisibleDurationSeconds, 8.45, 8.48);

        viewModel.PaperSpeedMillimetersPerSecond = 60;
        await WaitUntilAsync(() => Math.Abs(viewModel.VisibleDurationSeconds - (254d / 60d)) < 0.01);
        Assert.InRange(viewModel.VisibleDurationSeconds, 4.22, 4.25);
    }

    [Fact]
    public async Task TimebaseMode_UsesSelectedScreenDurationIndependentlyOfViewportWidth()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration), durationSeconds: 120);
        await using var viewModel = new RecordingReviewViewModel(reader, catalog);
        await viewModel.InitializeAsync();

        viewModel.UpdateViewportWidth(960);
        viewModel.TimebaseSecondsPerScreen = 15;
        viewModel.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;
        await WaitUntilAsync(() => Math.Abs(viewModel.VisibleDurationSeconds - 15) < 0.01);

        viewModel.UpdateViewportWidth(640);
        await Task.Delay(50);
        Assert.InRange(viewModel.VisibleDurationSeconds, 14.99, 15.01);
        Assert.Equal(30, viewModel.PaperSpeedMillimetersPerSecond);
    }

    [Fact]
    public async Task TimebaseMode_WorksBeforeTheWaveformHostReportsItsWidth()
    {
        var configuration = Configuration();
        var acquisition = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var reader = new FakeReader(Manifest(configuration), durationSeconds: 120);
        await using var viewModel = new RecordingReviewViewModel(reader, catalog);
        await viewModel.InitializeAsync();

        viewModel.TimebaseSecondsPerScreen = 5;
        viewModel.TimebaseSecondsPerScreen = 15;
        viewModel.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;

        await WaitUntilAsync(() => Math.Abs(viewModel.VisibleDurationSeconds - 15) < 0.01);
        Assert.InRange(viewModel.VisibleDurationSeconds, 14.99, 15.01);
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

    private sealed class FakeReader(LocalRawRecordingManifest manifest, double durationSeconds = 20) : IRecordingReviewReader
    {
        private int readCount;

        public LocalRawRecordingManifest Manifest { get; } = manifest;

        public double DurationSeconds => durationSeconds;

        public int ReadCount => Volatile.Read(ref readCount);

        public Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCount);
            return Task.FromResult(new RecordingReviewWindow(
                startSeconds,
                startSeconds,
                startSeconds + durationSeconds,
                [new RecordingReviewSegment(0, 1, 2, [1e-6, 0])]));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "Expected asynchronous display duration update was not completed.");
    }
}

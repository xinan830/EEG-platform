using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Review;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewSessionTests
{
    [Fact]
    public async Task SwitchingFromTwentyToThirteenOutputsPreservesPositionAndWindow()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var alternate = Profile("替代导联", configuration, 1);
        var reader = new FakeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition), new CompatibleRecordingMontage(alternate)],
            []);
        await using var session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds: 10);

        await session.InitializeAsync();
        await session.SeekAsync(125);
        await session.SelectViewingMontageAsync(alternate);

        Assert.Same(acquisition, session.AcquisitionMontage);
        Assert.Same(alternate, session.SelectedViewingMontage);
        Assert.Equal(125, session.PositionSeconds);
        Assert.Equal(10, session.VisibleDurationSeconds);
        Assert.Single(session.CurrentFrame!.OutputChannelNames);
    }

    [Fact]
    public async Task OlderWindowCompletionCannotReplaceNewerMontageSelection()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var alternate = Profile("替代导联", configuration, 1);
        var firstRead = new TaskCompletionSource<RecordingReviewWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRead = new TaskCompletionSource<RecordingReviewWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeReader(300, Manifest(configuration), firstRead, secondRead);
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition), new CompatibleRecordingMontage(alternate)],
            []);
        await using var session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds: 10);

        var initial = session.InitializeAsync();
        var alternateLoad = session.SelectViewingMontageAsync(alternate);
        firstRead.SetResult(Window());
        secondRead.SetResult(Window());
        await Task.WhenAll(initial, alternateLoad);

        Assert.Same(alternate, session.SelectedViewingMontage);
        Assert.Equal(alternate.Name, session.CurrentFrame!.ViewingMontageName);
    }

    [Fact]
    public async Task FilterChangeRebuildsOneWindowWithoutMutatingRawValues()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new FakeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var filter = new FakeFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        await session.InitializeAsync();
        await session.SetFilterAsync(new RecordingReviewFilterSettings(0.5, 40, null));

        // The visible cache spans two fixed 10 s filtered-source chunks. A
        // changed filter fingerprint rebuilds both chunks without touching raw.
        Assert.Equal(4, filter.CallCount);
        Assert.Equal(new RecordingReviewFilterSettings(0.5, 40, null), filter.LastSettings);
        Assert.Equal(1e-6, filter.LastSourceValue);
    }

    [Fact]
    public async Task SeekingInsideTheBoundedCacheDoesNotReadOrFilterAgain()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var filter = new FakeFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        await session.InitializeAsync();
        await session.LoadViewportAsync(5, 5);

        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(2, filter.CallCount);
        Assert.Equal(5, session.PositionSeconds);
        Assert.Equal(5, session.ViewportStartSeconds);
    }

    [Fact]
    public async Task ReturningToAnAlreadyFilteredViewportReusesTheDerivedFrame()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var filter = new FakeFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        await session.InitializeAsync();
        await session.SetFilterAsync(new RecordingReviewFilterSettings(0.5, 40, null));
        await session.SetFilterAsync(new RecordingReviewFilterSettings(1, 30, 50));

        Assert.Equal(4, filter.CallCount);
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task SwitchingViewingMontageReusesCompletedFilteredSourceChunks()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var alternate = Profile("替代导联", configuration, 1);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition), new CompatibleRecordingMontage(alternate)],
            []);
        var filter = new FakeFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        await session.InitializeAsync();
        await session.SelectViewingMontageAsync(alternate);

        Assert.Equal(2, filter.CallCount);
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(alternate.Name, session.CurrentFrame!.ViewingMontageName);
    }

    [Fact]
    public async Task NewDragTargetJoinsAnInFlightFilteredChunkInsteadOfRestartingIt()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var filter = new BlockingFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        var initial = session.InitializeAsync();
        await filter.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var moved = session.LoadViewportAsync(5, 5);
        filter.Release.TrySetResult();
        await Task.WhenAll(initial, moved);

        Assert.Equal(2, filter.CallCount);
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task TrackClickKeepsExistingFrameUntilTargetFrameIsComplete()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new TrackClickBlockingReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        await using var session = new RecordingReviewSession(reader, catalog, 10);

        await session.InitializeAsync();
        var initialFrame = session.CurrentFrame;
        var navigation = session.NavigateFromTrackClickAsync(100, 102.5);
        await reader.TargetReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, session.PositionSeconds);
        Assert.Equal(0, session.ViewportStartSeconds);
        Assert.Same(initialFrame, session.CurrentFrame);

        reader.ReleaseTarget.TrySetResult();
        Assert.True(await navigation);
        Assert.Equal(102.5, session.PositionSeconds);
        Assert.Equal(100, session.ViewportStartSeconds);
        Assert.NotSame(initialFrame, session.CurrentFrame);
    }

    [Fact]
    public async Task CachedTrackClickCommitsWithoutAnotherRead()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        await using var session = new RecordingReviewSession(reader, catalog, 10);

        await session.InitializeAsync();
        Assert.True(await session.NavigateFromTrackClickAsync(100, 102.5));
        var readsAfterFirstJump = reader.ReadCount;

        Assert.True(await session.NavigateFromTrackClickAsync(100, 102.5));

        Assert.Equal(readsAfterFirstJump, reader.ReadCount);
    }

    [Fact]
    public async Task NonInitialReviewWindowWarmsTheFilterFromContiguousRawSamples()
    {
        var configuration = Configuration(2);
        var acquisition = Profile("采集导联", configuration, 2);
        var reader = new RangeReader(300, Manifest(configuration));
        var catalog = new RecordingMontageCatalogResult(
            acquisition,
            AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3", "F4"]),
            [new CompatibleRecordingMontage(acquisition)],
            []);
        var filter = new WarmupCapturingFilter();
        await using var session = new RecordingReviewSession(reader, catalog, 10, filter);

        await session.SeekAsync(100);

        var warmup = Assert.IsType<RecordingReviewWindow>(filter.LastWarmup);
        var displayed = Assert.IsType<RecordingReviewWindow>(filter.LastSource);
        Assert.Equal(displayed.Segments[0].FirstSampleCounter, warmup.Segments[0].LastSampleCounter + 1L);
        Assert.True(warmup.ActualEndSeconds <= displayed.ActualStartSeconds);
    }

    private static RecordingReviewWindow Window() => new(
        0,
        0,
        0.004,
        [new RecordingReviewSegment(0, 2, 3, [1e-6, 2e-6, 0, 3e-6, 4e-6, 0])]);

    private static LocalRawRecordingManifest Manifest(ChannelConfigurationProfile configuration) => new(
        Guid.NewGuid(),
        "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "V",
        "device",
        "device",
        500,
        2,
        DateTimeOffset.UtcNow,
        configuration.Channels.Select((channel, index) => new AcquisitionChannel(index, index, channel.ElectrodeLabel, channel.ExpectedKind, "V"))
            .Append(new AcquisitionChannel(configuration.Channels.Count, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count"))
            .ToArray(),
        new AcquisitionProjectContext("p", "P001", "P", "C:\\p", "{}"),
        new Dictionary<string, string>());

    private static ChannelConfigurationProfile Configuration(int count) => new(
        "channel",
        "通道",
        "",
        ChannelConfigurationSource.User,
        "device",
        Enumerable.Range(0, count).Select(index => new ChannelConfigurationEntry(index, AcquisitionChannelKind.Reference, $"F{index + 3}", true, index)).ToArray(),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static MontageProfile Profile(string name, ChannelConfigurationProfile configuration, int outputCount)
    {
        var rows = configuration.Channels.Take(outputCount).Select((channel, index) => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.ElectrodeLabel, MontageNegativeKind.OriginalHardwareReference, null),
            channel.ElectrodeLabel,
            MontageNegativeKind.OriginalHardwareReference,
            [],
            index)).ToArray();
        var fingerprint = ChannelConfigurationFingerprint.Create(configuration);
        return new MontageProfile(
            name,
            name,
            "",
            MontageProfileSource.User,
            "device",
            fingerprint,
            configuration,
            rows,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(fingerprint, rows));
    }

    private sealed class FakeReader : IRecordingReviewReader
    {
        private readonly TaskCompletionSource<RecordingReviewWindow>? first;
        private readonly TaskCompletionSource<RecordingReviewWindow>? second;
        private int callCount;

        public FakeReader(
            double durationSeconds,
            LocalRawRecordingManifest manifest,
            TaskCompletionSource<RecordingReviewWindow>? first = null,
            TaskCompletionSource<RecordingReviewWindow>? second = null)
        {
            DurationSeconds = durationSeconds;
            Manifest = manifest;
            this.first = first;
            this.second = second;
        }

        public LocalRawRecordingManifest Manifest { get; }

        public double DurationSeconds { get; }

        public Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1 && first is not null)
            {
                return first.Task;
            }

            if (call == 2 && second is not null)
            {
                return second.Task;
            }

            return Task.FromResult(Window());
        }
    }

    private sealed class FakeFilter : IRecordingReviewFilter
    {
        public int CallCount { get; private set; }

        public RecordingReviewFilterSettings? LastSettings { get; private set; }

        public double LastSourceValue { get; private set; }

        public Task<RecordingReviewWindow> ApplyAsync(
            RecordingReviewWindow source,
            LocalRawRecordingManifest manifest,
            RecordingReviewFilterSettings settings,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSettings = settings;
            LastSourceValue = source.Segments[0].SampleMajorValues[0];
            var copied = source.Segments.Select(segment =>
                segment with { SampleMajorValues = segment.SampleMajorValues.ToArray() }).ToArray();
            return Task.FromResult(source with { Segments = copied });
        }
    }

    private sealed class RangeReader(double durationSeconds, LocalRawRecordingManifest manifest) : IRecordingReviewReader
    {
        private int readCount;

        public LocalRawRecordingManifest Manifest { get; } = manifest;

        public double DurationSeconds { get; } = durationSeconds;

        public int ReadCount => Volatile.Read(ref readCount);

        public Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCount);
            var firstSample = (long)Math.Round(startSeconds * Manifest.SamplingRateHz);
            var sampleCount = (int)Math.Ceiling(durationSeconds * Manifest.SamplingRateHz);
            var values = new double[sampleCount * 3];
            for (var index = 0; index < sampleCount; index++)
            {
                values[index * 3] = 1e-6;
                values[index * 3 + 1] = 2e-6;
            }
            return Task.FromResult(new RecordingReviewWindow(
                startSeconds,
                startSeconds,
                startSeconds + durationSeconds,
                [new RecordingReviewSegment(firstSample, sampleCount, 3, values)]));
        }
    }

    private sealed class TrackClickBlockingReader(double durationSeconds, LocalRawRecordingManifest manifest) : IRecordingReviewReader
    {
        private int readCount;

        public LocalRawRecordingManifest Manifest { get; } = manifest;
        public double DurationSeconds { get; } = durationSeconds;
        public TaskCompletionSource TargetReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseTarget { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref readCount) > 1)
            {
                TargetReadStarted.TrySetResult();
                await ReleaseTarget.Task.WaitAsync(cancellationToken);
            }

            var firstSample = (long)Math.Round(startSeconds * Manifest.SamplingRateHz);
            var sampleCount = (int)Math.Ceiling(durationSeconds * Manifest.SamplingRateHz);
            var values = new double[sampleCount * 3];
            for (var index = 0; index < sampleCount; index++)
            {
                values[index * 3] = 1e-6;
                values[index * 3 + 1] = 2e-6;
            }

            return new RecordingReviewWindow(startSeconds, startSeconds, startSeconds + durationSeconds,
                [new RecordingReviewSegment(firstSample, sampleCount, 3, values)]);
        }
    }

    private sealed class WarmupCapturingFilter : IRecordingReviewFilter
    {
        public RecordingReviewWindow? LastWarmup { get; private set; }
        public RecordingReviewWindow? LastSource { get; private set; }

        public Task<RecordingReviewWindow> ApplyAsync(RecordingReviewWindow source,
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(source);

        public Task<RecordingReviewWindow> ApplyWithWarmupAsync(RecordingReviewWindow? warmup,
            RecordingReviewWindow source, LocalRawRecordingManifest manifest,
            RecordingReviewFilterSettings settings, CancellationToken cancellationToken)
        {
            LastWarmup = warmup;
            LastSource = source;
            return Task.FromResult(source);
        }
    }

    private sealed class BlockingFilter : IRecordingReviewFilter
    {
        private int callCount;
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref callCount);

        public Task<RecordingReviewWindow> ApplyAsync(RecordingReviewWindow source,
            LocalRawRecordingManifest manifest, RecordingReviewFilterSettings settings, CancellationToken cancellationToken) =>
            ApplyWithWarmupAsync(null, source, manifest, settings, cancellationToken);

        public async Task<RecordingReviewWindow> ApplyWithWarmupAsync(RecordingReviewWindow? warmup,
            RecordingReviewWindow source, LocalRawRecordingManifest manifest,
            RecordingReviewFilterSettings settings, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                FirstCallStarted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return source;
        }
    }
}

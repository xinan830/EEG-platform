using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
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
}

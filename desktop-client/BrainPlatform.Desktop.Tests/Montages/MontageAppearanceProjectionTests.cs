using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageAppearanceProjectionTests
{
    [Theory]
    [InlineData(MontageNegativeKind.Channel)]
    [InlineData(MontageNegativeKind.Mean)]
    [InlineData(MontageNegativeKind.SpecifiedPair)]
    public void ReorderingAndColorNeverChangeReferenceFormulaValues(MontageNegativeKind kind)
    {
        var profile = MontageAppearanceTests.Profile();
        var outputs = profile.DerivedChannels.Select(channel => channel with
        {
            NegativeKind = kind,
            NegativeLabels = kind == MontageNegativeKind.Channel
                ? [channel.PositiveLabel == "F3" ? "F7" : "F3"] : ["F3", "F7"],
            Name = MontageDisplay.CreateName(channel.PositiveLabel, kind,
                channel.PositiveLabel == "F3" ? "F7" : "F3"),
        }).ToArray();
        profile = profile with { DerivedChannels = outputs, AverageReferenceLabels = ["F3", "F7"] };
        var reordered = profile with { DerivedChannels = outputs.Select(channel => channel with
            { DisplayOrder = 1 - channel.DisplayOrder, Color = "#ABCDEF" }).ToArray() };
        MontageValidation.Validate(reordered.ChannelConfigurationSnapshot, reordered.DerivedChannels,
            reordered.AverageReferenceLabels);
        var raw = new[] { 10e-6, 2e-6, 0d, 20e-6, 3e-6, 1d };
        var window = new RecordingReviewWindow(0, 0, 0.2, [new RecordingReviewSegment(0, 2, 3, raw)]);
        var baseline = MontageDisplayProjector.Project(window, Manifest(profile), profile)[0].Channels;
        var changed = MontageDisplayProjector.Project(window, Manifest(profile), reordered)[0].Channels;
        Assert.Equal(baseline.Select(channel => channel.Label).Reverse(), changed.Select(channel => channel.Label));
        foreach (var channel in changed)
            Assert.Equal(baseline.Single(item => item.Label == channel.Label).Values, channel.Values);
        Assert.Equal(new[] { 10e-6, 2e-6, 0d, 20e-6, 3e-6, 1d }, raw);
    }

    [Fact]
    public void LiveColorsAndOrderFollowOutputsNotNativeIndexes()
    {
        var profile = ColoredProfile();
        var manifest = Manifest(profile);
        var raw = new[] { 10e-6, 2e-6, 0d, 20e-6, 3e-6, 1d };
        var copy = raw.ToArray();
        var channels = new LiveDisplayChannel[]
            { new(0, 3, "F3", "Reference", true), new(1, 7, "F7", "Reference", true) };
        var source = new LiveWaveformSource(new AcquisitionStreamMetadata("device", "device", 10,
            manifest.Channels, 2, DateTimeOffset.UnixEpoch),
            [new AcquisitionBatch(0, 2, 3, raw, DateTimeOffset.UnixEpoch)], channels,
            MontageProfile: profile, MontageSourceChannels: channels);

        var frame = WaveformDisplayFrameBuilder.Build(source, 1, 500)!;
        Assert.Equal(new[] { "F7-REF", "F3-REF" }, frame.Traces.Select(trace => trace.Label));
        Assert.Equal(new[] { "#445566", "#112233" }, frame.Traces.Select(trace => trace.Color));
        Assert.Equal(2e-6, frame.Traces[0].Points[0].MinVolts);
        Assert.Equal(10e-6, frame.Traces[1].Points[0].MinVolts);
        var hidden = WaveformDisplayFrameBuilder.Build(source with
            { VisibleMontageChannelNames = new HashSet<string> { "F3-REF" } }, 1, 500)!;
        Assert.Equal("#112233", Assert.Single(hidden.Traces).Color);
        Assert.Equal(copy, raw);
    }

    [Fact]
    public async Task ReviewDefaultsToSnapshotAndRefreshesSameNamesWithNewColors()
    {
        var saved = ColoredProfile();
        var changed = saved with
        {
            DerivedChannels = saved.DerivedChannels.Select(row => row with { Color = "#778899" }).ToArray(),
        };
        changed = changed with { Fingerprint = MontageProfileFingerprint.Create(changed.ChannelConfigurationFingerprint,
            changed.DerivedChannels) };
        var reader = new Reader(Manifest(saved));
        var catalog = RecordingMontageCatalog.Build(reader.Manifest, [changed]);
        Assert.Equal(AcquisitionMontageStatus.Available, catalog.AcquisitionMontageStatus);
        await using var session = new RecordingReviewSession(reader, catalog, 5);
        await session.InitializeAsync();

        Assert.Equal(new[] { "F7-REF", "F3-REF" }, session.CurrentFrame!.OutputChannelNames);
        Assert.Equal(new[] { "#445566", "#112233" }, session.CurrentFrame.OutputChannelColors);
        Assert.Equal(2e-6, session.CurrentFrame.Segments[0].Channels[0].Values[0]);
        Assert.Equal(10e-6, session.CurrentFrame.Segments[0].Channels[1].Values[0]);
        await session.SeekAsync(8);
        await session.SelectViewingMontageAsync(changed);
        Assert.Equal(8, session.PositionSeconds);
        Assert.Equal(new[] { "#778899", "#778899" }, session.CurrentFrame!.OutputChannelColors);
        await session.SelectViewingMontageAsync(catalog.AcquisitionMontage);
        Assert.Equal(new[] { "#445566", "#112233" }, session.CurrentFrame!.OutputChannelColors);
    }

    [Fact]
    public async Task LegacySnapshotUsesSharedFallbackAndStillProjectsRealSources()
    {
        var legacy = MontageAppearanceTests.Profile();
        var reader = new Reader(Manifest(legacy));
        var catalog = RecordingMontageCatalog.Build(reader.Manifest, []);
        await using var session = new RecordingReviewSession(reader, catalog, 5);
        await session.InitializeAsync();
        Assert.Equal(new[] { WaveformTraceColors.DefaultAt(0), WaveformTraceColors.DefaultAt(1) },
            session.CurrentFrame!.OutputChannelColors);
        Assert.Equal(10e-6, session.CurrentFrame.Segments[0].Channels[0].Values[0]);
    }

    private static MontageProfile ColoredProfile()
    {
        var profile = MontageAppearanceTests.Profile();
        var outputs = new[] { profile.DerivedChannels[0] with { DisplayOrder = 1, Color = "#112233" },
            profile.DerivedChannels[1] with { DisplayOrder = 0, Color = "#445566" } };
        return profile with { DerivedChannels = outputs,
            Fingerprint = MontageProfileFingerprint.Create(profile.ChannelConfigurationFingerprint, outputs) };
    }

    private static LocalRawRecordingManifest Manifest(MontageProfile profile) => new(
        Guid.NewGuid(), "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units", "V",
        "device", "device", 10, 2, DateTimeOffset.UnixEpoch,
        [new(0, 3, "F3", AcquisitionChannelKind.Reference, "V"),
            new(1, 7, "F7", AcquisitionChannelKind.Reference, "V"),
            new(2, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
        new AcquisitionProjectContext("p", "P001", "P", "C:\\test", "{}"),
        new Dictionary<string, string>
        {
            ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
            ["channel_configuration_snapshot_json"] = JsonSerializer.Serialize(profile.ChannelConfigurationSnapshot),
        });

    private sealed class Reader(LocalRawRecordingManifest manifest) : IRecordingAccessSession
    {
        public LocalRawRecordingManifest Manifest => manifest;
        public double DurationSeconds => 30;

        public Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds,
            CancellationToken cancellationToken)
        {
            var count = (int)Math.Round(durationSeconds * manifest.SamplingRateHz);
            var values = Enumerable.Range(0, count).SelectMany(index => new[] { 10e-6, 2e-6, (double)index }).ToArray();
            return Task.FromResult(new RecordingReviewWindow(startSeconds, startSeconds, startSeconds + durationSeconds,
                [new RecordingReviewSegment((long)Math.Round(startSeconds * manifest.SamplingRateHz), count, 3, values)]));
        }

        public Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes,
            CancellationToken cancellationToken) =>
            RecordingAccessTestHelpers.ReadAsync(
                manifest, 0, ReadWindowAsync, range, channelIndexes, cancellationToken);
    }
}

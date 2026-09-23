using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class MontageDisplayProjectorTests
{
    [Fact]
    public void ProjectorComputesDirectAndMeanReferencesWithoutMutatingRawWindow()
    {
        var configuration = Configuration("F3", "F4", "M1");
        var direct = Profile(configuration, MontageNegativeKind.Channel, ["M1"]);
        var mean = Profile(configuration, MontageNegativeKind.Mean, ["F3", "F4"]);
        var manifest = Manifest(configuration);
        var raw = new RecordingReviewWindow(
            0,
            0,
            0.002,
            [new RecordingReviewSegment(0, 2, 4, [
                10e-6, 20e-6, 2e-6, 0,
                30e-6, 40e-6, 4e-6, 0])]);
        var rawCopy = raw.Segments[0].SampleMajorValues.ToArray();

        var directResult = MontageDisplayProjector.Project(raw, manifest, direct);
        var meanResult = MontageDisplayProjector.Project(raw, manifest, mean);

        Assert.Equal(2, directResult[0].Channels.Count);
        Assert.Equal(8e-6, directResult[0].Channels[0].Values[0], 12);
        Assert.Equal(18e-6, directResult[0].Channels[1].Values[0], 12);
        Assert.Equal(3, meanResult[0].Channels.Count);
        Assert.Equal(-5e-6, meanResult[0].Channels[0].Values[0], 12);
        Assert.Equal(5e-6, meanResult[0].Channels[1].Values[0], 12);
        Assert.Equal(-13e-6, meanResult[0].Channels[2].Values[0], 12);
        Assert.Equal(rawCopy, raw.Segments[0].SampleMajorValues);
    }

    [Fact]
    public void ProjectorPreservesGapSegmentsAndSupportsDifferentOutputCounts()
    {
        var configuration = Configuration("F3", "F4", "M1");
        var profile = Profile(configuration, MontageNegativeKind.Channel, ["M1"]);
        var manifest = Manifest(configuration);
        var window = new RecordingReviewWindow(
            0,
            0,
            0.008,
            [
                new RecordingReviewSegment(0, 1, 4, [1, 2, 3, 0]),
                new RecordingReviewSegment(4, 1, 4, [5, 6, 7, 0]),
            ]);

        var result = MontageDisplayProjector.Project(window, manifest, profile);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Channels.Count);
        Assert.Equal(0, result[0].FirstSampleCounter);
        Assert.Equal(4, result[1].FirstSampleCounter);
    }

    private static LocalRawRecordingManifest Manifest(ChannelConfigurationProfile configuration) => new(
        Guid.NewGuid(),
        "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "V",
        "device",
        "device",
        500,
        3,
        DateTimeOffset.UtcNow,
        configuration.Channels.Select((channel, index) => new AcquisitionChannel(
                index,
                channel.NativeChannelIndex,
                channel.ElectrodeLabel,
                channel.ExpectedKind,
                "V"))
            .Append(new AcquisitionChannel(3, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count"))
            .ToArray(),
        new AcquisitionProjectContext("p", "P001", "P", "C:\\p", "{}"),
        new Dictionary<string, string>());

    private static ChannelConfigurationProfile Configuration(params string[] labels) => new(
        "channel",
        "通道",
        "",
        ChannelConfigurationSource.User,
        "device",
        labels.Select((label, index) => new ChannelConfigurationEntry(index, AcquisitionChannelKind.Reference, label, true, index)).ToArray(),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static MontageProfile Profile(
        ChannelConfigurationProfile configuration,
        MontageNegativeKind kind,
        IReadOnlyList<string> negatives)
    {
        var singleNegative = kind == MontageNegativeKind.Channel ? negatives.Single() : null;
        var rows = configuration.Channels
            .Where(channel => singleNegative is null || !string.Equals(channel.ElectrodeLabel, singleNegative, StringComparison.OrdinalIgnoreCase))
            .Select((channel, index) => new DerivedMontageChannel(
                MontageDisplay.CreateName(channel.ElectrodeLabel, kind, singleNegative),
                channel.ElectrodeLabel,
                kind,
                negatives,
                index))
            .ToArray();
        var fingerprint = ChannelConfigurationFingerprint.Create(configuration);
        return new MontageProfile(
            "montage",
            "导联",
            "",
            MontageProfileSource.User,
            "device",
            fingerprint,
            configuration,
            rows,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(fingerprint, rows, kind == MontageNegativeKind.Mean ? negatives : null),
            kind == MontageNegativeKind.Mean ? negatives : []);
    }
}

using System.Text.Json;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingMontageCatalogTests
{
    [Fact]
    public void BuildUsesEmbeddedAcquisitionSnapshotAndExcludesMissingSources()
    {
        var channelConfiguration = ChannelConfiguration("F3", "F4", "M1", "M2");
        var acquisition = Profile("acquisition", "采集导联 A", channelConfiguration, MontageNegativeKind.OriginalHardwareReference);
        var compatible = Profile("compatible", "M1/M2参考", channelConfiguration, MontageNegativeKind.SpecifiedPair, ["M1", "M2"]);
        var missing = Profile("missing", "需要 AF3", channelConfiguration, MontageNegativeKind.Channel, ["AF3"]);
        var manifest = Manifest(
            channelConfiguration,
            JsonSerializer.Serialize(acquisition));

        var result = RecordingMontageCatalog.Build(manifest, [compatible, missing]);

        Assert.Equal("采集导联 A", result.AcquisitionMontage?.Name);
        Assert.Contains(result.CompatibleViewingMontages, item => item.Profile.Name == "M1/M2参考");
        Assert.DoesNotContain(result.CompatibleViewingMontages, item => item.Profile.Name == "需要 AF3");
        Assert.Contains(result.Incompatible, item => item.Name == "需要 AF3" && item.Reason.Contains("AF3"));
    }

    [Fact]
    public void BuildUsesTheRecordedSnapshotInstanceWhenCurrentProfileHasTheSameIdentity()
    {
        var channelConfiguration = ChannelConfiguration("F3", "F4");
        var acquisition = Profile("acquisition", "采集导联", channelConfiguration, MontageNegativeKind.OriginalHardwareReference);
        var currentProfile = acquisition with { ChannelSnapshotStatusLabel = "可用" };
        var result = RecordingMontageCatalog.Build(
            Manifest(channelConfiguration, JsonSerializer.Serialize(acquisition)),
            [currentProfile]);

        Assert.Same(result.AcquisitionMontage, result.CompatibleViewingMontages.Single().Profile);
    }

    [Fact]
    public void BuildRejectsDuplicateSignalLabelsForMontageSources()
    {
        var channelConfiguration = ChannelConfiguration("F3", "M1", "M1");
        var profile = Profile("m1", "M1参考", channelConfiguration, MontageNegativeKind.Channel, ["M1"]);

        var result = RecordingMontageCatalog.Build(Manifest(channelConfiguration), [profile]);

        Assert.DoesNotContain(result.CompatibleViewingMontages, item => item.Profile.Name == "M1参考");
        Assert.Contains(result.Incompatible, item => item.Reason.Contains("重复"));
    }

    [Fact]
    public void BuildDoesNotInventAcquisitionMontageWhenSnapshotIsMissing()
    {
        var channelConfiguration = ChannelConfiguration("F3", "F4");
        var result = RecordingMontageCatalog.Build(Manifest(channelConfiguration), []);

        Assert.Null(result.AcquisitionMontage);
        Assert.Equal(AcquisitionMontageStatus.MissingSnapshot, result.AcquisitionMontageStatus);
        Assert.Contains("未记录", result.AcquisitionMontageStatusText);
        Assert.Equal(2, result.RawSignalView.ChannelLabels.Count);
    }

    private static LocalRawRecordingManifest Manifest(
        ChannelConfigurationProfile channelConfiguration,
        string? montageSnapshot = null)
    {
        var hardware = new Dictionary<string, string>();
        if (montageSnapshot is not null)
        {
            hardware["montage_configuration_snapshot_json"] = montageSnapshot;
        }

        var channels = channelConfiguration.Channels
            .Select((channel, index) => new AcquisitionChannel(
                index,
                channel.NativeChannelIndex,
                channel.ElectrodeLabel,
                channel.ExpectedKind,
                "V"))
            .Append(new AcquisitionChannel(
                channelConfiguration.Channels.Count,
                99,
                "Counter",
                AcquisitionChannelKind.SampleCounter,
                "count"))
            .ToArray();
        return new LocalRawRecordingManifest(
            Guid.NewGuid(),
            "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
            "V",
            "device",
            "device",
            500,
            channels[^1].StreamIndex,
            DateTimeOffset.UtcNow,
            channels,
            new AcquisitionProjectContext("project", "P001", "Project", "C:\\project", "{}"),
            hardware);
    }

    private static ChannelConfigurationProfile ChannelConfiguration(params string[] labels) => new(
        "channel-a",
        "通道 A",
        "测试通道",
        ChannelConfigurationSource.User,
        "device",
        labels.Select((label, index) => new ChannelConfigurationEntry(
            index,
            AcquisitionChannelKind.Reference,
            label,
            true,
            index)).ToArray(),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static MontageProfile Profile(
        string id,
        string name,
        ChannelConfigurationProfile channelConfiguration,
        MontageNegativeKind kind,
        IReadOnlyList<string>? negativeLabels = null)
    {
        var negatives = negativeLabels ?? [];
        var singleNegative = kind == MontageNegativeKind.Channel ? negatives.SingleOrDefault() : null;
        var rows = channelConfiguration.Channels
            .Where(channel => kind != MontageNegativeKind.Channel || !string.Equals(channel.ElectrodeLabel, singleNegative, StringComparison.OrdinalIgnoreCase))
            .Select((channel, index) => new DerivedMontageChannel(
                MontageDisplay.CreateName(channel.ElectrodeLabel, kind, singleNegative),
                channel.ElectrodeLabel,
                kind,
                negatives,
                index))
            .ToArray();
        var channelFingerprint = ChannelConfigurationFingerprint.Create(channelConfiguration);
        return new MontageProfile(
            id,
            name,
            "测试导联",
            MontageProfileSource.User,
            "device",
            channelFingerprint,
            channelConfiguration,
            rows,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(channelFingerprint, rows));
    }
}

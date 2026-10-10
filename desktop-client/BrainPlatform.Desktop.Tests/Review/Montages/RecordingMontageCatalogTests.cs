using System.Text.Json;

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
            JsonSerializer.Serialize(acquisition),
            includeChannelSnapshot: true);

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
            Manifest(channelConfiguration, JsonSerializer.Serialize(acquisition), includeChannelSnapshot: true),
            [currentProfile]);

        Assert.Same(result.AcquisitionMontage, result.CompatibleViewingMontages.Single().Profile);
    }

    [Fact]
    public void BuildRejectsDuplicateSignalLabelsForMontageSources()
    {
        var channelConfiguration = ChannelConfiguration("F3", "M1", "M1");
        var profile = Profile("m1", "M1参考", channelConfiguration, MontageNegativeKind.Channel, ["M1"]);

        var result = RecordingMontageCatalog.Build(Manifest(channelConfiguration, includeChannelSnapshot: true), [profile]);

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

    [Fact]
    public void BuildRejectsSwappedNativeMappingEvenWhenLabelsArePresent()
    {
        var recordedConfiguration = ChannelConfiguration("F3", "F7");
        var swappedConfiguration = ChannelConfiguration("F7", "F3");
        var swapped = Profile("swapped", "交换映射", swappedConfiguration,
            MontageNegativeKind.OriginalHardwareReference);

        var result = RecordingMontageCatalog.Build(
            Manifest(recordedConfiguration, includeChannelSnapshot: true),
            [swapped]);

        Assert.DoesNotContain(result.CompatibleViewingMontages, item => item.Profile.Name == "交换映射");
        Assert.Contains(result.Incompatible,
            item => item.Name == "交换映射" && item.Reason.Contains("物理通道映射"));
    }

    [Fact]
    public void BuildRejectsSameMappingFromDifferentChannelConfiguration()
    {
        var recordedConfiguration = ChannelConfiguration("F3", "F7");
        var candidateConfiguration = recordedConfiguration with
        {
            Id = "channel-copy",
            Name = "复制通道",
            Channels = recordedConfiguration.Channels.Select(channel => channel with
            {
                DisplayOrder = channel.DisplayOrder + 10,
            }).ToArray(),
        };
        // The physical mapping is equal, but the configuration provenance is
        // different and must not be mixed in the review selector.
        var candidate = Profile("copy", "同映射不同身份", candidateConfiguration,
            MontageNegativeKind.OriginalHardwareReference) with
        {
            DerivedChannels = Profile("copy-rows", "同映射不同身份", candidateConfiguration,
                    MontageNegativeKind.OriginalHardwareReference)
                .DerivedChannels
                .Select((channel, index) => channel with { DisplayOrder = index + 10 })
                .ToArray(),
        };

        var result = RecordingMontageCatalog.Build(
            Manifest(recordedConfiguration, includeChannelSnapshot: true),
            [candidate]);

        Assert.DoesNotContain(result.CompatibleViewingMontages, item => item.Profile.Name == "同映射不同身份");
        Assert.Contains(result.Incompatible,
            item => item.Name == "同映射不同身份" && item.Reason.Contains("采集时使用的通道配置"));
    }

    [Fact]
    public void BuildRejectsMontageWhenHistoricalSnapshotIsMissing()
    {
        var configuration = ChannelConfiguration("F3", "F7");
        var profile = Profile("p", "无历史快照", configuration,
            MontageNegativeKind.OriginalHardwareReference);

        var result = RecordingMontageCatalog.Build(Manifest(configuration), [profile]);

        Assert.DoesNotContain(result.CompatibleViewingMontages, item => item.Profile.Name == "无历史快照");
        Assert.Contains(result.Incompatible,
            item => item.Name == "无历史快照" && item.Reason.Contains("无法验证"));
    }

    [Fact]
    public void BuildChecksEveryMeanReferenceDependency()
    {
        var recordedConfiguration = ChannelConfiguration("F3", "F7", "Cz");
        var swappedConfiguration = ChannelConfiguration("F3", "Cz", "F7");
        var candidate = Profile("mean", "平均参考", swappedConfiguration,
            MontageNegativeKind.Mean, ["F7", "Cz"], ["F7", "Cz"]);

        var result = RecordingMontageCatalog.Build(
            Manifest(recordedConfiguration, includeChannelSnapshot: true),
            [candidate]);

        Assert.Contains(result.Incompatible,
            item => item.Name == "平均参考" && item.Reason.Contains("物理通道映射"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildRejectsActualSourceThatDisagreesWithHistoricalSnapshot(bool changeNativeIndex)
    {
        var configuration = ChannelConfiguration("F3", "F7");
        var profile = Profile("p", "源通道异常", configuration, MontageNegativeKind.OriginalHardwareReference);
        var manifest = Manifest(configuration, includeChannelSnapshot: true);
        manifest = manifest with
        {
            Channels = manifest.Channels.Select(channel => channel.Label == "F3"
                ? changeNativeIndex
                    ? channel with { NativeChannelIndex = 42 }
                    : channel with { Kind = AcquisitionChannelKind.Bipolar }
                : channel).ToArray(),
        };

        var result = RecordingMontageCatalog.Build(manifest, [profile]);

        Assert.Empty(result.CompatibleViewingMontages);
        Assert.Contains("实际源通道", Assert.Single(result.Incompatible).Reason);
    }

    private static LocalRawRecordingManifest Manifest(
        ChannelConfigurationProfile channelConfiguration,
        string? montageSnapshot = null,
        bool includeChannelSnapshot = false)
    {
        var hardware = new Dictionary<string, string>();
        if (includeChannelSnapshot)
        {
            hardware["channel_configuration_snapshot_json"] = JsonSerializer.Serialize(channelConfiguration);
        }
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
        IReadOnlyList<string>? negativeLabels = null,
        IReadOnlyList<string>? averageReferenceLabels = null)
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
            MontageProfileFingerprint.Create(channelFingerprint, rows, averageReferenceLabels),
            averageReferenceLabels);
    }
}

using System.Text.Json;
using Xunit;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class PopProductPresentationTests
{
    [Fact]
    public void HistoricalSystemProfiles_DisplayPopWithoutRewritingSnapshots()
    {
        var channels = CreateChannels();
        var presets = SystemMontageProfileFactory.Create(channels);
        Assert.All(presets, profile => Assert.StartsWith("POP默认通道配置 ", profile.Name));
        var preset = presets.First();
        var historical = preset with { Name = "ANT默认通道配置 REF参考", Description = "历史 ANT 描述" };
        var channelFingerprint = ChannelConfigurationFingerprint.Create(channels);
        var montageFingerprint = historical.Fingerprint;

        Assert.Equal("POP默认通道配置", channels.DisplayName);
        Assert.Equal("POP默认通道配置 REF参考", historical.DisplayName);
        Assert.DoesNotContain("ANT", historical.DisplayDescription);
        Assert.Equal("POP默认通道配置", historical.ChannelConfigurationName);
        var json = JsonSerializer.Serialize(historical);
        var saved = JsonSerializer.Deserialize<MontageProfile>(json)!;
        Assert.Equal(historical.Name, saved.Name);
        Assert.Equal(historical.Description, saved.Description);
        Assert.Equal(channels.Name, saved.ChannelConfigurationSnapshot.Name);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty(nameof(MontageProfile.DisplayName), out _));
        Assert.False(document.RootElement.TryGetProperty(nameof(MontageProfile.DisplayDescription), out _));
        Assert.Equal(channelFingerprint, ChannelConfigurationFingerprint.Create(saved.ChannelConfigurationSnapshot));
        Assert.Equal(montageFingerprint, saved.Fingerprint);
        Assert.Equal(montageFingerprint, MontageProfileFingerprint.Create(
            saved.ChannelConfigurationFingerprint, saved.DerivedChannels, saved.AverageReferenceLabels));
    }

    [Fact]
    public void UserNames_ArePreservedEvenWhenTheyLookLikeSystemPresets()
    {
        var channels = CreateChannels() with { Source = ChannelConfigurationSource.User };
        var preset = SystemMontageProfileFactory.Create(CreateChannels()).First();
        var user = preset with { Id = "user", Source = MontageProfileSource.User, Name = "ANT默认通道配置 REF参考" };

        Assert.Equal(channels.Name, channels.DisplayName);
        Assert.Equal(channels.Description, channels.DisplayDescription);
        Assert.Equal(user.Name, user.DisplayName);
        Assert.Equal(user.Description, user.DisplayDescription);
        Assert.Equal(2, MontageProfileCatalogue.Deduplicate([preset, user]).Count);
    }

    [Fact]
    public void SystemDeduplication_UsesMappingAndFormulasRatherThanNames()
    {
        var preset = SystemMontageProfileFactory.Create(CreateChannels()).First();
        var renamed = preset with { Id = "historical-system", Name = "任意旧名称" };
        Assert.Equal(preset.DisplayName, renamed.DisplayName);
        Assert.Same(preset, Assert.Single(MontageProfileCatalogue.Deduplicate([preset, renamed])));
        var remapped = renamed with
        {
            ChannelConfigurationSnapshot = renamed.ChannelConfigurationSnapshot with
            {
                Channels = renamed.ChannelConfigurationSnapshot.Channels.Select((channel, index) =>
                    index == 0 ? channel with { NativeChannelIndex = 27 } : channel).ToArray(),
            },
        };
        Assert.Equal(2, MontageProfileCatalogue.Deduplicate([preset, remapped]).Count);
        var changedFormula = renamed with
        {
            DerivedChannels = renamed.DerivedChannels.Select((channel, index) => index == 0
                ? channel with { NegativeKind = MontageNegativeKind.Channel, NegativeLabels = ["Cz"] } : channel).ToArray(),
        };
        Assert.Equal(2, MontageProfileCatalogue.Deduplicate([preset, changedFormula]).Count);
    }

    [Fact]
    public void DriverPresentation_UsesPopAndKeepsNativeIdentity()
    {
        var descriptor = new AntEegoAcquisitionDriver().Descriptor;
        Assert.Equal("ant-eego", descriptor.DriverId);
        Assert.Equal("POP", descriptor.DisplayName);
        Assert.DoesNotContain("ANT", descriptor.SetupDescription);
        Assert.Equal("POP SN123", DeviceProductPresentation.FormatDeviceName("SN123"));
        Assert.Equal("POP amplifier", DeviceProductPresentation.FormatDeviceName(null));
        Assert.Equal("POP", DeviceProductPresentation.ForDriver("ANT-EEGO", "未连接"));
        Assert.Equal("未连接", DeviceProductPresentation.ForDriver(null, "未连接"));
        Assert.Equal("other-driver", DeviceProductPresentation.ForDriver("other-driver", "未连接"));
        var error = Assert.Throws<AntEegoSdkException>(() => AntEegoNativeApi.Load("missing-pop-sdk.dll"));
        Assert.Equal("ANT_EEGO_SDK_NOT_FOUND", error.Code);
        using var adapter = new AntEegoAcquisitionAdapter(new AntEegoAdapterOptions("", null, null));
        Assert.Contains("POP", adapter.GetReadiness().Detail);
        Assert.DoesNotContain("ANT", adapter.GetReadiness().Detail);
    }

    private static ChannelConfigurationProfile CreateChannels() => new(
        "system-ant-standard-28-input", "ANT默认通道配置", "历史 ANT 描述",
        ChannelConfigurationSource.System, "ant-eego|28",
        [new(0, AcquisitionChannelKind.Reference, "F3", true, 0),
         new(1, AcquisitionChannelKind.Reference, "Cz", true, 1),
         new(2, AcquisitionChannelKind.Reference, "M1", true, 2),
         new(3, AcquisitionChannelKind.Reference, "M2", true, 3)],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}

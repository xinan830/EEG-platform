using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveMontageCoordinatorTests
{
    [Fact]
    public void Select_AdvancesRevisionAndKeepsOrderedOutputsAtEffectiveSample()
    {
        var initial = Profile("montage-a", "fingerprint-a", "F3-REF", "F4-REF");
        var next = Profile("montage-b", "fingerprint-b", "F4-AVG", "F3-AVG");
        var coordinator = new LiveMontageCoordinator(initial);

        var snapshot = coordinator.Select(next, effectiveFromSampleCounter: 128);

        Assert.Equal(2, snapshot.Revision);
        Assert.Equal(128, snapshot.EffectiveFromSampleCounter);
        Assert.Equal("fingerprint-b", snapshot.Fingerprint);
        Assert.Equal(["F3-AVG", "F4-AVG"], snapshot.OrderedChannelNames);
        Assert.Equal("montage-b", coordinator.Current.Profile.Id);
        Assert.Equal(1, coordinator.Capture(127).Revision);
        Assert.Equal(2, coordinator.Capture(128).Revision);
    }

    [Fact]
    public void Select_RejectsDifferentChannelConfigurationAndOlderBoundary()
    {
        var initial = Profile("montage-a", "fingerprint-a", "F3-REF");
        var coordinator = new LiveMontageCoordinator(initial);

        Assert.Throws<InvalidOperationException>(() => coordinator.Select(
            Profile("incompatible", "incompatible-profile", ["F3-REF"], "other-config"), 1));

        var next = Profile("montage-b", "fingerprint-b", "F3-AVG");
        coordinator.Select(next, 128);
        Assert.Throws<InvalidOperationException>(() => coordinator.Select(initial, 127));
        Assert.Equal(2, coordinator.Current.Revision);
    }

    private static MontageProfile Profile(
        string id,
        string identity,
        params string[] channelNames) => Profile(id, identity, channelNames, "shared-channel-config");

    private static MontageProfile Profile(
        string id,
        string identity,
        string[] channelNames,
        string channelConfiguration)
    {
        var now = DateTimeOffset.UnixEpoch;
        var config = new ChannelConfigurationProfile(
            "channels", "Channels", "", ChannelConfigurationSource.User, "device", [], now, now);
        var channels = channelNames.Select((name, index) => new DerivedMontageChannel(
            name, name.Split('-')[0], MontageNegativeKind.OriginalHardwareReference, [], channelNames.Length - index)).ToArray();
        return new MontageProfile(
            id, id, "", MontageProfileSource.User, "device", channelConfiguration, config,
            channels, 1, now, now, identity);
    }
}

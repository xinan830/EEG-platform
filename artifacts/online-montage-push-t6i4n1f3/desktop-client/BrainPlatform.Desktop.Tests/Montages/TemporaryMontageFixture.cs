namespace BrainPlatform.Desktop.Tests;

internal static class TemporaryMontageFixture
{
    public static MontageProfile Create(params string[] labels)
    {
        var source = new ChannelConfigurationProfile("online-source", "当前通道", "", ChannelConfigurationSource.User,
            "device", labels.Select((label, index) => new ChannelConfigurationEntry(index,
                AcquisitionChannelKind.Reference, label, true, index)).ToArray(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return Create(source);
    }

    public static MontageProfile Create(ChannelConfigurationProfile source)
    {
        var labels = source.Channels.Where(channel => channel.IsSelectedForDisplay)
            .OrderBy(channel => channel.DisplayOrder).Select(channel => channel.ElectrodeLabel).ToArray();
        var outputs = labels.Select((label, index) => labels.Length == 1
            ? new DerivedMontageChannel($"{label}-REF", label, MontageNegativeKind.OriginalHardwareReference, [], index, "#123456")
            : new DerivedMontageChannel($"{label}-{labels[(index + 1) % labels.Length]}", label,
                MontageNegativeKind.Channel, [labels[(index + 1) % labels.Length]], index, "#123456")).ToArray();
        var fingerprint = ChannelConfigurationFingerprint.Create(source);
        return new MontageProfile("online-test", "在线导联", "", MontageProfileSource.User, source.DeviceSignature,
            fingerprint, source, outputs, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            MontageProfileFingerprint.Create(fingerprint, outputs));
    }
}

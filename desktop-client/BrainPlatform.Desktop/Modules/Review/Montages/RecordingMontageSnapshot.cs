namespace BrainPlatform.Desktop.Modules.Review.Montages;

/// <summary>Resolves display identities from complete recorded mappings, never from current device settings.</summary>
internal static class RecordingMontageSnapshot
{
    public static MontageProfile Resolve(MontageProfile? profile)
    {
        var snapshot = profile?.ChannelConfigurationSnapshot;
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.DeviceSignature) ||
            snapshot is null || string.IsNullOrWhiteSpace(snapshot.Id) ||
            string.IsNullOrWhiteSpace(snapshot.DeviceSignature) ||
            string.IsNullOrWhiteSpace(snapshot.HardwareReferenceElectrodeLocation) ||
            string.IsNullOrWhiteSpace(snapshot.HardwareGroundElectrodeLocation) ||
            snapshot.Channels is not { Count: > 0 } ||
            snapshot.Channels.Any(channel => channel is null || channel.ElectrodeLabel is null ||
                (channel.ElectrodeLocation is { } location && location.CanonicalSite is null)) ||
            profile.DerivedChannels is not { Count: > 0 } ||
            profile.DerivedChannels.Any(channel => channel is null ||
                string.IsNullOrWhiteSpace(channel.Name) || string.IsNullOrWhiteSpace(channel.PositiveLabel) ||
                channel.NegativeLabels is null || channel.NegativeLabels.Any(string.IsNullOrWhiteSpace)) ||
            (profile.AverageReferenceLabels?.Any(string.IsNullOrWhiteSpace) ?? false))
            throw new InvalidOperationException("导联快照缺少有效的身份、通道配置或导联公式，无法安全生成显示标识。");

        MontageValidation.Validate(snapshot, profile.DerivedChannels, profile.AverageReferenceLabels);
        // Imported semantic snapshots need not carry workstation save-time digests.
        var channelFingerprint = string.IsNullOrWhiteSpace(profile.ChannelConfigurationFingerprint)
            ? ChannelConfigurationFingerprint.Create(snapshot)
            : profile.ChannelConfigurationFingerprint;
        return profile with
        {
            Description = profile.Description ?? string.Empty,
            ChannelConfigurationFingerprint = channelFingerprint,
            Fingerprint = string.IsNullOrWhiteSpace(profile.Fingerprint)
                ? MontageProfileFingerprint.Create(channelFingerprint, profile.DerivedChannels, profile.AverageReferenceLabels)
                : profile.Fingerprint,
        };
    }
}

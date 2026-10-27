namespace BrainPlatform.Desktop.Modules.Montages.Validation;

/// <summary>Source validity is independent of historical edit locks and archival.</summary>
internal static class MontageProfileAvailability
{
    public static MontageProfile Evaluate(MontageProfile profile,
        IEnumerable<ChannelConfigurationProfile> sources, string? currentDeviceSignature)
    {
        var snapshotFingerprint = ChannelConfigurationFingerprint.Create(profile.ChannelConfigurationSnapshot);
        var legacySnapshotFingerprint = ChannelConfigurationFingerprint.CreateLegacyIncludingManualDiagramPosition(profile.ChannelConfigurationSnapshot);
        var montageFingerprint = MontageProfileFingerprint.Create(snapshotFingerprint,
            profile.DerivedChannels, profile.AverageReferenceLabels);
        var legacyMontageFingerprint = MontageProfileFingerprint.Create(legacySnapshotFingerprint,
            profile.DerivedChannels, profile.AverageReferenceLabels);
        var snapshotMatches = profile.ChannelConfigurationFingerprint is var stored &&
            (stored == snapshotFingerprint || stored == legacySnapshotFingerprint);
        if (!snapshotMatches || (montageFingerprint != profile.Fingerprint && legacyMontageFingerprint != profile.Fingerprint))
            return WithStatus(profile, "配置无效", "保存的通道快照或导联内容与其指纹不一致，不能作为可信配置使用。");

        if (currentDeviceSignature is not null && profile.DeviceSignature != currentDeviceSignature)
            return WithStatus(profile, "设备不匹配", "此导联配置绑定的物理通道能力与当前设备不同。");

        var source = sources.FirstOrDefault(candidate => candidate.Id == profile.ChannelConfigurationSnapshot.Id);
        if (source is null)
            return WithStatus(profile, "来源已删除", "原通道配置已删除；导联仍保留创建时的完整通道快照。");

        var sourceFingerprint = ChannelConfigurationFingerprint.Create(source);
        var legacySourceFingerprint = ChannelConfigurationFingerprint.CreateLegacyIncludingManualDiagramPosition(source);
        return sourceFingerprint == profile.ChannelConfigurationFingerprint ||
            legacySourceFingerprint == profile.ChannelConfigurationFingerprint
            ? WithStatus(profile, "已同步", "导联绑定的通道快照与当前通道配置一致。")
            : WithStatus(profile, "来源已修改", "原通道配置已有未同步修改；此导联继续使用创建时保存的旧快照。");
    }

    private static MontageProfile WithStatus(MontageProfile profile, string label, string detail) => profile with
    {
        ChannelSnapshotStatusLabel = label,
        ChannelSnapshotStatusDetail = detail,
    };
}

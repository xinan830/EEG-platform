namespace BrainPlatform.Desktop.Configuration;

/// <summary>
/// Builds read-only system montage presets from the validated ANT default
/// channel configuration. Presets are data definitions; they never alter raw
/// acquisition values or the scientific analysis reference.
/// </summary>
public static class SystemMontageProfileFactory
{
    public static IReadOnlyList<MontageProfile> Create(ChannelConfigurationProfile channelConfiguration)
    {
        ArgumentNullException.ThrowIfNull(channelConfiguration);
        if (channelConfiguration.Source != ChannelConfigurationSource.System
            || !string.Equals(channelConfiguration.Id, "system-ant-standard-28-input", StringComparison.Ordinal))
        {
            return [];
        }

        var sourceChannels = channelConfiguration.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => (Label: channel.ElectrodeLabel.Trim(), Order: channel.DisplayOrder))
            .ToArray();
        if (sourceChannels.Length == 0)
        {
            return [];
        }

        var labels = sourceChannels.Select(channel => channel.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fingerprint = ChannelConfigurationFingerprint.Create(channelConfiguration);
        var profiles = new List<MontageProfile>
        {
            CreateIdentity(channelConfiguration, sourceChannels, fingerprint),
            CreateReferenceProfile(channelConfiguration, fingerprint, sourceChannels,
                "average", "平均参考", "系统默认；使用 ANT 默认通道配置中全部命名 EEG 通道的平均值作为软件参考。",
                MontageNegativeKind.Mean, sourceChannels.Select(channel => channel.Label).ToArray()),
        };

        if (labels.Contains("M1") && labels.Contains("M2"))
        {
            profiles.Add(CreateReferenceProfile(
                channelConfiguration, fingerprint, sourceChannels,
                "m1-m2", "M1/M2参考", "系统默认；使用 M1 与 M2 的平均值作为软件参考。",
                MontageNegativeKind.SpecifiedPair, ["M1", "M2"]));
        }

        if (labels.Contains("Cz"))
        {
            profiles.Add(CreateChannelReferenceProfile(
                channelConfiguration, fingerprint, sourceChannels,
                "cz", "Cz参考", "系统默认；以 Cz 作为软件参考。Cz 自身不生成 Cz-Cz 输出。", "Cz"));
        }

        profiles.Add(CreateBipolarProfile(
            channelConfiguration, fingerprint, sourceChannels,
            "longitudinal-bipolar", "纵向双极", "系统默认；按 10–20 前后方向生成纵向双极导联。",
            [
                ("Fp1", "F7"), ("F7", "T3"), ("T3", "T5"), ("T5", "O1"),
                ("Fp2", "F8"), ("F8", "T4"), ("T4", "T6"), ("T6", "O2"),
                ("Fp1", "F3"), ("F3", "C3"), ("C3", "P3"), ("P3", "O1"),
                ("Fp2", "F4"), ("F4", "C4"), ("C4", "P4"), ("P4", "O2"),
            ]));
        profiles.Add(CreateBipolarProfile(
            channelConfiguration, fingerprint, sourceChannels,
            "transverse-bipolar", "横向双极", "系统默认；按 10–20 左右方向生成横向双极导联。",
            [
                ("Fp1", "Fp2"),
                ("F7", "F3"), ("F3", "Fz"), ("Fz", "F4"), ("F4", "F8"),
                ("T3", "C3"), ("C3", "Cz"), ("Cz", "C4"), ("C4", "T4"),
                ("T5", "P3"), ("P3", "Pz"), ("Pz", "P4"), ("P4", "T6"),
                ("O1", "O2"),
            ]));

        return profiles;
    }

    private static MontageProfile CreateIdentity(
        ChannelConfigurationProfile source,
        IReadOnlyList<(string Label, int Order)> channels,
        string fingerprint)
    {
        var derived = channels.Select(channel => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.Label, MontageNegativeKind.OriginalHardwareReference, null),
            channel.Label, MontageNegativeKind.OriginalHardwareReference, [], channel.Order)).ToArray();
        return CreateProfile(source, fingerprint, "ref", "REF参考",
            "系统默认；ANT 默认通道配置的原始 EEG 输出，使用硬件 REF，不执行软件重参考。", derived);
    }

    private static MontageProfile CreateReferenceProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        string suffix,
        string description,
        MontageNegativeKind kind,
        IReadOnlyList<string> referenceLabels)
    {
        var derived = channels.Select(channel => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.Label, kind, null),
            channel.Label, kind, referenceLabels, channel.Order)).ToArray();
        return CreateProfile(source, fingerprint, key, suffix, description, derived, referenceLabels);
    }

    private static MontageProfile CreateChannelReferenceProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        string suffix,
        string description,
        string referenceLabel)
    {
        var derived = channels
            .Where(channel => !string.Equals(channel.Label, referenceLabel, StringComparison.OrdinalIgnoreCase))
            .Select(channel => new DerivedMontageChannel(
                MontageDisplay.CreateName(channel.Label, MontageNegativeKind.Channel, referenceLabel),
                channel.Label, MontageNegativeKind.Channel, [referenceLabel], channel.Order)).ToArray();
        return CreateProfile(source, fingerprint, key, suffix, description, derived);
    }

    private static MontageProfile CreateBipolarProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        string suffix,
        string description,
        IReadOnlyList<(string Positive, string Negative)> pairs)
    {
        var labels = channels.Select(channel => channel.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derived = pairs
            .Where(pair => labels.Contains(pair.Positive) && labels.Contains(pair.Negative))
            .Select((pair, index) => new DerivedMontageChannel(
                MontageDisplay.CreateName(pair.Positive, MontageNegativeKind.Channel, pair.Negative),
                pair.Positive, MontageNegativeKind.Channel, [pair.Negative], index)).ToArray();
        return CreateProfile(source, fingerprint, key, suffix, description, derived);
    }

    private static MontageProfile CreateProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        string key,
        string suffix,
        string description,
        IReadOnlyList<DerivedMontageChannel> derived,
        IReadOnlyList<string>? referenceLabels = null) => new(
        $"system-ant-{key}", $"ANT默认通道配置 {suffix}", description,
        MontageProfileSource.System, source.DeviceSignature, fingerprint, source, derived,
        1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        MontageProfileFingerprint.Create(fingerprint, derived, referenceLabels), referenceLabels ?? []);
}

namespace BrainPlatform.Desktop.Modules.Montages.Domain;

/// <summary>
/// Builds read-only system montage presets from the validated default
/// channel configuration. Presets are data definitions; they never alter raw
/// acquisition values or the scientific analysis reference.
/// </summary>
public static class SystemMontageProfileFactory
{
    private static readonly IReadOnlyDictionary<string, (string Suffix, string Description)> Presets =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["system-ant-ref"] = ("REF参考", $"系统默认；{DeviceProductPresentation.Brand} 默认通道配置的原始 EEG 输出，使用硬件 REF，不执行软件重参考。"),
            ["system-ant-average"] = ("平均参考", $"系统默认；使用 {DeviceProductPresentation.Brand} 默认通道配置中全部命名 EEG 通道的平均值作为软件参考。"),
            ["system-ant-m1-m2"] = ("M1/M2参考", "系统默认；使用 M1 与 M2 的平均值作为软件参考。"),
            ["system-ant-cz"] = ("Cz参考", "系统默认；以 Cz 作为软件参考。Cz 自身不生成 Cz-Cz 输出。"),
            ["system-ant-longitudinal-bipolar"] = ("纵向双极", "系统默认；按 10–20 前后方向生成纵向双极导联。"),
            ["system-ant-transverse-bipolar"] = ("横向双极", "系统默认；按 10–20 左右方向生成横向双极导联。"),
        };

    public static string DisplayNameFor(MontageProfile profile) => ResolvePresetId(profile) is { } id
        ? $"{DefaultChannelLabelTemplate.DisplayName} {Presets[id].Suffix}" : profile.Name;

    public static string DisplayDescriptionFor(MontageProfile profile) => ResolvePresetId(profile) is { } id
        ? Presets[id].Description : profile.Description;

    private static string? ResolvePresetId(MontageProfile profile)
    {
        if (profile.Source != MontageProfileSource.System ||
            !DefaultChannelLabelTemplate.IsSystemProfile(profile.ChannelConfigurationSnapshot)) return null;
        if (Presets.ContainsKey(profile.Id)) return profile.Id;
        var identity = MontageProfileCatalogue.CreateSystemPresetSemanticIdentity(profile);
        return Create(profile.ChannelConfigurationSnapshot).FirstOrDefault(preset =>
            MontageProfileCatalogue.CreateSystemPresetSemanticIdentity(preset) == identity)?.Id;
    }

    public static IReadOnlyList<MontageProfile> Create(ChannelConfigurationProfile channelConfiguration)
    {
        ArgumentNullException.ThrowIfNull(channelConfiguration);
        if (!DefaultChannelLabelTemplate.IsSystemProfile(channelConfiguration))
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
                "average",
                MontageNegativeKind.Mean, sourceChannels.Select(channel => channel.Label).ToArray()),
        };

        if (labels.Contains("M1") && labels.Contains("M2"))
        {
            profiles.Add(CreateReferenceProfile(
                channelConfiguration, fingerprint, sourceChannels,
                "m1-m2",
                MontageNegativeKind.SpecifiedPair, ["M1", "M2"]));
        }

        if (labels.Contains("Cz"))
        {
            profiles.Add(CreateChannelReferenceProfile(
                channelConfiguration, fingerprint, sourceChannels,
                "cz", "Cz"));
        }

        profiles.Add(CreateBipolarProfile(
            channelConfiguration, fingerprint, sourceChannels,
            "longitudinal-bipolar",
            [
                ("Fp1", "F7"), ("F7", "T3"), ("T3", "T5"), ("T5", "O1"),
                ("Fp2", "F8"), ("F8", "T4"), ("T4", "T6"), ("T6", "O2"),
                ("Fp1", "F3"), ("F3", "C3"), ("C3", "P3"), ("P3", "O1"),
                ("Fp2", "F4"), ("F4", "C4"), ("C4", "P4"), ("P4", "O2"),
            ]));
        profiles.Add(CreateBipolarProfile(
            channelConfiguration, fingerprint, sourceChannels,
            "transverse-bipolar",
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
        return CreateProfile(source, fingerprint, "ref", derived);
    }

    private static MontageProfile CreateReferenceProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        MontageNegativeKind kind,
        IReadOnlyList<string> referenceLabels)
    {
        var derived = channels.Select(channel => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.Label, kind, null),
            channel.Label, kind, referenceLabels, channel.Order)).ToArray();
        return CreateProfile(source, fingerprint, key, derived, referenceLabels);
    }

    private static MontageProfile CreateChannelReferenceProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        string referenceLabel)
    {
        var derived = channels
            .Where(channel => !string.Equals(channel.Label, referenceLabel, StringComparison.OrdinalIgnoreCase))
            .Select(channel => new DerivedMontageChannel(
                MontageDisplay.CreateName(channel.Label, MontageNegativeKind.Channel, referenceLabel),
                channel.Label, MontageNegativeKind.Channel, [referenceLabel], channel.Order)).ToArray();
        return CreateProfile(source, fingerprint, key, derived);
    }

    private static MontageProfile CreateBipolarProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        IReadOnlyList<(string Label, int Order)> channels,
        string key,
        IReadOnlyList<(string Positive, string Negative)> pairs)
    {
        var labels = channels.Select(channel => channel.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derived = pairs
            .Where(pair => labels.Contains(pair.Positive) && labels.Contains(pair.Negative))
            .Select((pair, index) => new DerivedMontageChannel(
                MontageDisplay.CreateName(pair.Positive, MontageNegativeKind.Channel, pair.Negative),
                pair.Positive, MontageNegativeKind.Channel, [pair.Negative], index)).ToArray();
        return CreateProfile(source, fingerprint, key, derived);
    }

    private static MontageProfile CreateProfile(
        ChannelConfigurationProfile source,
        string fingerprint,
        string key,
        IReadOnlyList<DerivedMontageChannel> derived,
        IReadOnlyList<string>? referenceLabels = null) => new(
        $"system-ant-{key}", $"{DefaultChannelLabelTemplate.DisplayName} {Presets[$"system-ant-{key}"].Suffix}",
        Presets[$"system-ant-{key}"].Description,
        MontageProfileSource.System, source.DeviceSignature, fingerprint, source, derived,
        1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        MontageProfileFingerprint.Create(fingerprint, derived, referenceLabels), referenceLabels ?? []);
}

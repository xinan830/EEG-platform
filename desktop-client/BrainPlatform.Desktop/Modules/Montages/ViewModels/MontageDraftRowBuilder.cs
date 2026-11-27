namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// Stateless helper that builds fixed, editable, or sparse montage draft rows
/// and computes average-reference membership from channel configuration profiles.
/// </summary>
internal static class MontageDraftRowBuilder
{
    public static bool HasOneOutputPerConfiguredSource(MontageProfile profile)
    {
        var sourceLabels = profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positiveLabels = profile.DerivedChannels
            .Select(channel => channel.PositiveLabel.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return profile.DerivedChannels.Count == sourceLabels.Count && sourceLabels.SetEquals(positiveLabels);
    }

    public static (IReadOnlyList<string> SourceLabels, IReadOnlyList<string> AverageLabels, IReadOnlyList<DerivedMontageChannel> Outputs)
        PrepareFixed(ChannelConfigurationProfile source, MontageProfile? existing)
    {
        var fixedChannels = source.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => (Label: channel.ElectrodeLabel.Trim(), channel.DisplayOrder))
            .ToArray();
        var sourceLabels = fixedChannels.Select(channel => channel.Label).ToArray();
        var averageLabels = existing?.AverageReferenceLabels is { Count: > 0 } savedAverageLabels
            ? savedAverageLabels
            : sourceLabels;
        var outputs = existing?.DerivedChannels ?? fixedChannels.Select(channel => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.Label, MontageNegativeKind.OriginalHardwareReference, null),
            channel.Label, MontageNegativeKind.OriginalHardwareReference, [], channel.DisplayOrder)).ToArray();
        return (sourceLabels, averageLabels, outputs);
    }

    public static (IReadOnlyList<string> SourceLabels, IReadOnlyList<string> AverageLabels, IReadOnlyList<DerivedMontageChannel> Outputs)
        PrepareFromExisting(MontageProfile profile)
    {
        var sourceLabels = profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToArray();
        var averageLabels = profile.AverageReferenceLabels ?? [];
        return (sourceLabels, averageLabels, profile.DerivedChannels);
    }

    public static MontageProfile BuildValidatedProfile(
        ChannelConfigurationProfile? draftChannelConfiguration,
        string draftName,
        string draftDescription,
        IReadOnlyList<MontageDraftRow> draftRows,
        IReadOnlyList<string> averageLabels,
        string? draftProfileId,
        DateTimeOffset? draftCreatedAtUtc,
        int? existingRevision)
    {
        var snapshot = draftChannelConfiguration ?? throw new InvalidOperationException("请选择适用的通道配置。");
        var name = draftName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("导联配置名称不能为空。");
        }

        var channels = draftRows.Select(row => row.ToModel(averageLabels)).ToArray();
        MontageValidation.Validate(snapshot, channels, averageLabels);
        var channelFingerprint = ChannelConfigurationFingerprint.Create(snapshot);
        var now = DateTimeOffset.UtcNow;
        return new MontageProfile(
            draftProfileId ?? Guid.NewGuid().ToString("N"),
            name,
            draftDescription.Trim(),
            MontageProfileSource.User,
            snapshot.DeviceSignature,
            channelFingerprint,
            snapshot,
            channels,
            draftProfileId is null ? 1 : (existingRevision ?? 0) + 1,
            draftCreatedAtUtc ?? now,
            now,
            MontageProfileFingerprint.Create(channelFingerprint, channels, averageLabels),
            averageLabels);
    }

    public static void ApplyReferenceToAll(
        IEnumerable<MontageDraftRow> rows,
        MontageNegativeKind kind,
        IReadOnlyList<string>? specifiedPair = null)
    {
        foreach (var row in rows)
        {
            if (specifiedPair is not null) row.SetSpecifiedPair(specifiedPair[0], specifiedPair[1]);
            row.NegativeKind = kind;
        }
    }
}

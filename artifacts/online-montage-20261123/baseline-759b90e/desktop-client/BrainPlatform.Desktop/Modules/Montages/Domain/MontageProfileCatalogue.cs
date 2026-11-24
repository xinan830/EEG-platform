namespace BrainPlatform.Desktop.Modules.Montages.Domain;

/// <summary>
/// Shared catalogue boundary for current, historical, and persisted montage
/// profiles. It removes duplicate identities without changing raw data or
/// historical snapshots.
/// </summary>
public static class MontageProfileCatalogue
{
    public static IReadOnlyList<MontageProfile> Deduplicate(
        IEnumerable<MontageProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var systemPresetIdentities = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MontageProfile>();
        foreach (var profile in profiles)
        {
            if (!identities.Add(profile.Id))
            {
                continue;
            }

            var systemPresetIdentity = CreateSystemPresetSemanticIdentity(profile);
            if (systemPresetIdentity is not null &&
                !systemPresetIdentities.Add(systemPresetIdentity))
            {
                continue;
            }

            result.Add(profile);
        }

        return result;
    }

    private static string? CreateSystemPresetSemanticIdentity(MontageProfile profile)
    {
        if (!profile.Name.StartsWith("ANT默认通道配置 ", StringComparison.Ordinal))
        {
            return null;
        }

        var derived = string.Join('|', profile.DerivedChannels
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => string.Join(':',
                channel.DisplayOrder,
                channel.PositiveLabel.Trim().ToUpperInvariant(),
                channel.NegativeKind,
                string.Join(',', channel.NegativeLabels
                    .Select(label => label.Trim().ToUpperInvariant())))));
        var average = string.Join(',', (profile.AverageReferenceLabels ?? [])
            .Select(label => label.Trim().ToUpperInvariant())
            .OrderBy(label => label, StringComparer.Ordinal));
        return string.Join('\u001F',
            profile.Name.Trim(),
            profile.DeviceSignature.Trim(),
            profile.ChannelConfigurationSnapshot.Id.Trim(),
            derived,
            average);
    }
}

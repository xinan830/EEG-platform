namespace BrainPlatform.Desktop.Modules.Montages.Domain;

/// <summary>Resolve colors from the selected snapshot, never a later preset.</summary>
public static class MontageOutputAppearance
{
    public static IReadOnlyList<string> ResolveColors(MontageProfile? profile, IReadOnlyList<string> outputNames)
    {
        var colors = profile?.DerivedChannels.OrderBy(channel => channel.DisplayOrder)
            .Select((channel, index) => (channel.Name, Color: WaveformTraceColors.Resolve(channel.Color, index)))
            .ToDictionary(item => item.Name, item => item.Color, StringComparer.OrdinalIgnoreCase);
        return outputNames.Select((name, index) => colors?.GetValueOrDefault(name)
            ?? WaveformTraceColors.DefaultAt(index)).ToArray();
    }
}

using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Views;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Montages.ViewModels;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public sealed record MontageDirectionArrow(string FromLabel, string ToLabel, double FromX, double FromY, double ToX, double ToY);
public sealed record MontageDirectionPoint(string Label, double X, double Y);

/// <summary>Projects display-montage equations onto configured electrode map locations.</summary>
public static class MontageDirectionProjection
{
    public static IReadOnlyList<MontageDirectionArrow> Create(
        ChannelConfigurationProfile configuration,
        IEnumerable<MontageDraftRow> rows,
        IReadOnlyList<string> averageReferenceLabels)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(rows);
        var positions = configuration.Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => (channel.ElectrodeLabel.Trim(), Position: channel.ManualMapPosition ?? ElectrodeMapProjection.ResolvePosition(channel.ElectrodeLabel, channel.ElectrodeLocation)))
            .Where(item => item.Position is not null)
            .ToDictionary(item => item.Item1, item => item.Position!, StringComparer.OrdinalIgnoreCase);
        positions["REF"] = configuration.ReferenceMapPosition ?? ElectrodeMapDefaults.Reference;
        positions["GND"] = configuration.GroundMapPosition ?? ElectrodeMapDefaults.Ground;

        var arrows = new List<MontageDirectionArrow>();
        foreach (var row in rows)
        {
            if (!positions.TryGetValue(row.PositiveLabel, out var positive)) continue;
            var model = row.ToModel(averageReferenceLabels);
            IEnumerable<string> targets = model.NegativeKind switch
            {
                MontageNegativeKind.OriginalHardwareReference => ["REF"],
                MontageNegativeKind.Channel => model.NegativeLabels,
                MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair => model.NegativeLabels,
                _ => [],
            };
            foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!positions.TryGetValue(target, out var negative) || string.Equals(target, row.PositiveLabel, StringComparison.OrdinalIgnoreCase)) continue;
                arrows.Add(new MontageDirectionArrow(row.PositiveLabel, target,
                    positive.X, positive.Y, negative.X, negative.Y));
            }
        }
        return arrows;
    }

    public static IReadOnlyList<MontageDirectionPoint> CreatePoints(ChannelConfigurationProfile configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var points = configuration.Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => (
                Label: channel.ElectrodeLabel.Trim(),
                Position: channel.ManualMapPosition ?? ElectrodeMapProjection.ResolvePosition(channel.ElectrodeLabel, channel.ElectrodeLocation)))
            .Where(item => item.Position is not null)
            .Select(item => new MontageDirectionPoint(item.Label, item.Position!.X, item.Position.Y))
            .ToList();
        points.Add(new MontageDirectionPoint("REF",
            configuration.ReferenceMapPosition?.X ?? ElectrodeMapDefaults.Reference.X,
            configuration.ReferenceMapPosition?.Y ?? ElectrodeMapDefaults.Reference.Y));
        points.Add(new MontageDirectionPoint("GND",
            configuration.GroundMapPosition?.X ?? ElectrodeMapDefaults.Ground.X,
            configuration.GroundMapPosition?.Y ?? ElectrodeMapDefaults.Ground.Y));
        return points;
    }

}

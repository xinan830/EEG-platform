namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>Build editable outputs from saved definitions without reordering sources.</summary>
internal static class MontageDraftRowFactory
{
    public static IReadOnlyList<MontageDraftRow> Create(
        ChannelConfigurationProfile source,
        IReadOnlyList<DerivedMontageChannel> outputs,
        IReadOnlyList<string> averageLabels,
        string averageSummary,
        string pairSummary)
    {
        var referenceOptions = source.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.ElectrodeLabel.Trim()).ToArray();
        return outputs.OrderBy(channel => channel.DisplayOrder).Select((channel, index) =>
        {
            var negatives = channel.NegativeLabels.Take(2).ToArray();
            return new MontageDraftRow(channel.PositiveLabel, channel.DisplayOrder, referenceOptions,
                channel.NegativeKind,
                channel.NegativeKind == MontageNegativeKind.Channel ? negatives.SingleOrDefault() : null,
                channel.NegativeKind == MontageNegativeKind.SpecifiedPair ? negatives.ElementAtOrDefault(0) : null,
                channel.NegativeKind == MontageNegativeKind.SpecifiedPair ? negatives.ElementAtOrDefault(1) : null,
                averageSummary, pairSummary, source.Channels, averageLabels,
                WaveformTraceColors.Resolve(channel.Color, index));
        }).ToArray();
    }
}

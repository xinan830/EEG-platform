namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

internal static class TnkOnDemandScoreStateProjection
{
    private static readonly (string Label, double Upper, string Color)[] Definitions =
    [
        ("自然调整", 55, "#9E9E9E"), ("平稳专注", 65, "#42A5F5"),
        ("轻度放松", 75, "#66BB6A"), ("深度沉浸", double.PositiveInfinity, "#8E63C7")
    ];

    internal static IReadOnlyList<TnkScoreState> Project(TnkChartSeries? series, double cursorSeconds)
    {
        var durations = new double[Definitions.Length];
        double? previousTime = null;
        foreach (var point in series?.Points ?? [])
        {
            if (point.X > cursorSeconds) break;
            if (point.Value is not double score || !double.IsFinite(score))
            {
                previousTime = null;
                continue;
            }
            if (previousTime is double previous)
            {
                var delta = Math.Max(0, point.X - previous);
                var state = Array.FindIndex(Definitions, definition => score < definition.Upper);
                if (state >= 0) durations[state] += delta;
            }
            previousTime = point.X;
        }
        var total = durations.Sum();
        return Definitions.Select((definition, index) => new TnkScoreState(
            definition.Label, definition.Color, durations[index],
            total > 0 ? durations[index] / total * 100 : 0)).ToArray();
    }
}

using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>Bounded display history of returned windows; never computes or invents scientific values.</summary>
internal sealed class TnkWindowChartHistory
{
    internal const int MaximumWindows = 600;
    private string? sessionId;
    private readonly SortedDictionary<double, IReadOnlyList<TnkChildResultDisplay>> windows = [];

    internal void Clear() { sessionId = null; windows.Clear(); }

    internal IReadOnlyList<TnkChildResultDisplay> Read(DynamicWindowResponse window,
        IReadOnlyList<TnkChildResultDisplay> current, double cadenceSeconds)
    {
        Add(window, current);
        return Project(current, cadenceSeconds);
    }

    internal void Add(DynamicWindowResponse window, IReadOnlyList<TnkChildResultDisplay> result)
    {
        if (sessionId != window.SessionId) { Clear(); sessionId = window.SessionId; }
        if (window.Range is not { } range || !double.IsFinite(range.EndSeconds)) return;
        if (window.State is not ("ready" or "insufficient_data" or "rejected" or "unavailable" or "failed")) return;
        windows[range.EndSeconds] = result;
        while (windows.Count > MaximumWindows) windows.Remove(windows.First().Key);
    }

    internal IReadOnlyList<TnkChildResultDisplay> Project(
        IReadOnlyList<TnkChildResultDisplay> current, double cadenceSeconds)
    {
        cadenceSeconds = double.IsFinite(cadenceSeconds) && cadenceSeconds > 0 ? cadenceSeconds : 1;
        return current.Select(child =>
        {
            var definitions = windows.Values.SelectMany(cards => cards.Where(card => card.AlgorithmId == child.AlgorithmId))
                .SelectMany(card => card.Chart.Series).DistinctBy(series => series.Name).ToArray();
            var series = definitions.Select(definition =>
            {
                var points = new SortedDictionary<double, TnkChartPoint>();
                double? previous = null;
                foreach (var (end, cards) in windows)
                {
                    if (previous is double last && end - last > cadenceSeconds * 1.5)
                        points[last + cadenceSeconds] = new(last + cadenceSeconds, null, "未计算区间");
                    var source = cards.FirstOrDefault(card => card.AlgorithmId == child.AlgorithmId)?.Chart.Series
                        .FirstOrDefault(item => item.Name == definition.Name);
                    var sourceChild = cards.FirstOrDefault(card => card.AlgorithmId == child.AlgorithmId);
                    if (sourceChild?.FailureCode == "TNK_UPDATE_NOT_DUE")
                    {
                        // This is a scheduled hold, not a failed/missing sample. Keep the
                        // previous backend value and its real timestamp; do not insert NaN.
                        previous = end;
                        continue;
                    }
                    var point = source?.Points.LastOrDefault();
                    var time = point?.X ?? end;
                    points[time] = new(time, point?.Value, $"{time:G8} s");
                    previous = end;
                }
                return definition with { Points = points.Values.ToArray() };
            }).ToArray();
            return child with { Chart = new(true, series) };
        }).ToArray();
    }
}

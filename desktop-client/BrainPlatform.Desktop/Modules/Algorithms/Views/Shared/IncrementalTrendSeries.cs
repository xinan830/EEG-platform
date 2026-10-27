using System.Collections;
using SciChart.Charting.Model.DataSeries;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

/// <summary>Appends new chronological trend points and rebuilds only after replacement or rewind.</summary>
internal sealed class IncrementalTrendSeries<TPoint>(
    XyDataSeries<double, double> dataSeries,
    Func<TPoint, (double X, double Y)> project)
{
    private (double X, double Y)[] rendered = [];

    internal bool Update(IEnumerable? source)
    {
        var next = source?.Cast<TPoint>().Select(project).ToArray() ?? [];
        var appendFrom = TrendSeriesAppendPlanner.FindAppendStart(rendered, next);
        if (appendFrom < 0)
        {
            dataSeries.Clear();
            Append(next, 0);
        }
        else
        {
            if (appendFrom == next.Length)
                return false;
            Append(next, appendFrom);
        }

        rendered = next;
        return true;
    }

    private void Append((double X, double Y)[] points, int start)
    {
        for (var index = start; index < points.Length; index++)
            dataSeries.Append(points[index].X, points[index].Y);
    }
}

internal static class TrendSeriesAppendPlanner
{
    /// <returns>The first new point index, or -1 when the current series must be replaced.</returns>
    internal static int FindAppendStart<T>(IReadOnlyList<T> previous, IReadOnlyList<T> next)
    {
        if (next.Count < previous.Count)
            return -1;
        for (var index = 0; index < previous.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(previous[index], next[index]))
                return -1;
        }
        return previous.Count;
    }
}

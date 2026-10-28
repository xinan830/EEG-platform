using System.Collections.ObjectModel;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

public sealed class RbpChannelHistory(string channel, IEnumerable<RbpWindowPoint> windows,
    IReadOnlyList<RbpBandPoint> bands)
{
    public string Channel { get; } = channel;
    public ObservableCollection<RbpWindowPoint> Windows { get; } = new(windows);
    public IReadOnlyList<RbpBandPoint> Bands { get; } = bands;

    internal void Append(RbpWindowPoint window, int maximumWindows)
    {
        var existing = Windows.FirstOrDefault(item =>
            item.StartSeconds == window.StartSeconds && item.EndSeconds == window.EndSeconds);
        if (existing is not null)
        {
            var index = Windows.IndexOf(existing);
            Windows[index] = window;
        }
        else
        {
            var insertAt = 0;
            while (insertAt < Windows.Count && Windows[insertAt].EndSeconds <= window.EndSeconds) insertAt++;
            Windows.Insert(insertAt, window);
        }
        while (Windows.Count > maximumWindows) Windows.RemoveAt(0);
    }
}

internal static class RbpHistoryProjection
{
    public static IReadOnlyList<RbpChannelHistory> FromFull(IEnumerable<RbpChannelWindowPoint> points) => Project(points);

    public static IReadOnlyList<RbpChannelHistory> FromVisited(IEnumerable<RbpChannelWindowPoint> points) => Project(points);

    public static void AppendVisited(IReadOnlyList<RbpChannelHistory> histories,
        IEnumerable<RbpChannelWindowPoint> points, int maximumWindows)
    {
        var byChannel = histories.ToDictionary(history => history.Channel, StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
            if (byChannel.TryGetValue(point.Channel, out var history))
                history.Append(point.Window, maximumWindows);
    }

    private static IReadOnlyList<RbpChannelHistory> Project(IEnumerable<RbpChannelWindowPoint> source)
    {
        var grouped = source.GroupBy(point => point.Channel, StringComparer.OrdinalIgnoreCase);
        return grouped.Select(group =>
        {
            var windows = group.Select(point => point.Window)
                .GroupBy(window => (window.StartSeconds, window.EndSeconds))
                .Select(items => items.Last())
                .OrderBy(window => window.EndSeconds)
                .ToArray();
            var latest = windows.LastOrDefault()?.Shares;
            var definitions = RbpResultPreview.BandDefinitions.Select((band, index) => band with
            { Share = latest is not null ? latest[index] : null }).ToArray();
            return new RbpChannelHistory(group.Key, windows, definitions);
        }).ToArray();
    }
}

using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>Bounded display history of returned windows; never computes or invents scientific values.</summary>
internal sealed class TnkWindowChartHistory
{
    internal const int MaximumWindows = 600;
    private string? sessionId;
    private readonly SortedDictionary<(double EndSeconds, double StartSeconds, string IdentityKey),
        IReadOnlyList<TnkChildResultDisplay>> windows = [];
    private readonly Dictionary<(double EndSeconds, double StartSeconds, string IdentityKey), string> windowFingerprints = [];
    private readonly SortedDictionary<(double EndSeconds, long FirstSequence, long LastSequence), TnkPlaybackGap> gaps = [];
    private readonly List<(long StartSample, long EndSample)> acceptedRanges = [];
    private bool historyTruncated;
    private bool reconciling;
    private readonly object gate = new();

    internal void Clear()
    {
        lock (gate)
        {
            sessionId = null;
            windows.Clear();
            windowFingerprints.Clear();
            gaps.Clear();
            acceptedRanges.Clear();
            historyTruncated = false;
            reconciling = false;
        }
    }

    internal void AddGap(TnkPlaybackGap gap)
    {
        if (!double.IsFinite(gap.EndSeconds) || gap.EndSeconds < gap.StartSeconds) return;
        lock (gate)
        {
            gaps[(gap.StartSeconds, gap.FirstMissingSequence, gap.LastMissingSequence)] = gap;
            reconciling = true;
        }
    }

    internal void BeginReconciliation() { lock (gate) reconciling = true; }
    internal void MarkReconciliationFailed() { lock (gate) reconciling = false; }

    internal string CoverageText
    {
        get
        {
            lock (gate)
                return reconciling ? "正在补齐历史区间"
                    : historyTruncated || gaps.Count > 0 ? "历史不完整 · 存在未补齐区间"
                    : windows.Count == 0 ? "尚无历史数据" : "历史完整 · 已接收窗口连续";
        }
    }

    internal double? LatestCompletedEndSeconds
    {
        get { lock (gate) return windows.Count == 0 ? null : windows.Last().Key.EndSeconds; }
    }

    internal IReadOnlyList<TnkChildResultDisplay> Read(DynamicWindowResponse window,
        IReadOnlyList<TnkChildResultDisplay> current, double cadenceSeconds)
    {
        Add(window, current);
        return Project(current, cadenceSeconds);
    }

    internal bool Add(DynamicWindowResponse window, IReadOnlyList<TnkChildResultDisplay> result)
    {
        lock (gate)
        {
            if (sessionId != window.SessionId)
            {
                sessionId = window.SessionId;
                windows.Clear();
                windowFingerprints.Clear();
                gaps.Clear();
                acceptedRanges.Clear();
                historyTruncated = false;
                reconciling = false;
            }
            if (window.Range is not { } range || !double.IsFinite(range.EndSeconds)) return false;
            if (window.State is not ("ready" or "partial" or "insufficient_data" or "rejected" or "unavailable" or "failed")) return false;
            var identityKey = window.Identity is { } identity
                ? CanonicalIdentityKey(identity)
                : $"{range.StartSeconds:R}:{range.EndSeconds:R}";
            var key = (range.EndSeconds, range.StartSeconds, identityKey);
            var fingerprint = System.Text.Json.JsonSerializer.Serialize(new
            {
                window.State,
                window.Quality,
                Result = result,
            });
            if (windowFingerprints.TryGetValue(key, out var previous) && previous == fingerprint)
                return false;
            if (!windows.ContainsKey(key)) acceptedRanges.Add((range.StartSample, range.EndSample));
            windows[key] = result;
            windowFingerprints[key] = fingerprint;
            ReconcileCoveredGaps();
            while (windows.Count > MaximumWindows)
            {
                var removed = windows.First().Key;
                windows.Remove(removed);
                windowFingerprints.Remove(removed);
                if (acceptedRanges.Count > MaximumWindows) acceptedRanges.RemoveAt(0);
                historyTruncated = true;
            }
            return true;
        }
    }

    private void ReconcileCoveredGaps()
    {
        foreach (var key in gaps.Keys.ToArray())
        {
            var gap = gaps[key];
            if (!gap.SampleRangeExact || gap.AffectedEndSample <= gap.AffectedStartSample) continue;
            var intervals = acceptedRanges.Where(range => range.EndSample > gap.AffectedStartSample &&
                    range.StartSample < gap.AffectedEndSample).OrderBy(range => range.StartSample).ToArray();
            var coveredThrough = gap.AffectedStartSample;
            foreach (var range in intervals)
            {
                if (range.StartSample > coveredThrough) break;
                coveredThrough = Math.Max(coveredThrough, range.EndSample);
                if (coveredThrough >= gap.AffectedEndSample) break;
            }
            var expectedEvents = gap.LastMissingSequence - gap.FirstMissingSequence + 1;
            if (coveredThrough >= gap.AffectedEndSample && expectedEvents > 0 && intervals.Length >= expectedEvents)
                gaps.Remove(key);
        }
        if (gaps.Count == 0) reconciling = false;
    }

    private static string CanonicalIdentityKey(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{System.Text.Json.JsonSerializer.Serialize(property.Name)}:{CanonicalIdentityKey(property.Value)}")) + "}",
        System.Text.Json.JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalIdentityKey)) + "]",
        _ => element.GetRawText(),
    };

    internal IReadOnlyList<TnkChildResultDisplay> Project(
        IReadOnlyList<TnkChildResultDisplay> current, double cadenceSeconds)
    {
        cadenceSeconds = double.IsFinite(cadenceSeconds) && cadenceSeconds > 0 ? cadenceSeconds : 1;
        KeyValuePair<(double EndSeconds, double StartSeconds, string IdentityKey),
            IReadOnlyList<TnkChildResultDisplay>>[] snapshot;
        KeyValuePair<(double EndSeconds, long FirstSequence, long LastSequence), TnkPlaybackGap>[] gapSnapshot;
        lock (gate)
        {
            snapshot = windows.ToArray();
            gapSnapshot = gaps.ToArray();
        }
        var latest = snapshot.Length > 0 ? snapshot[^1].Value : current;
        return latest.Select(child =>
        {
            var definitions = snapshot.SelectMany(item => item.Value.Where(card => card.AlgorithmId == child.AlgorithmId))
                .SelectMany(card => card.Chart.Series).DistinctBy(series => series.Name).ToArray();
            var series = definitions.Select(definition =>
            {
                var points = new SortedDictionary<double, TnkChartPoint>();
                double? previous = null;
                foreach (var (key, cards) in snapshot)
                {
                    var end = key.EndSeconds;
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
                foreach (var (key, gap) in gapSnapshot)
                {
                    var label = gap.SampleRangeExact
                        ? $"投递断档：序列 {gap.FirstMissingSequence}-{gap.LastMissingSequence}，样本 {gap.AffectedStartSample}-{gap.AffectedEndSample}"
                        : $"投递断档：序列 {gap.FirstMissingSequence}-{gap.LastMissingSequence}，范围估计";
                    points[key.EndSeconds] = new(key.EndSeconds, null, label);
                }
                return definition with { Points = points.Values.ToArray() };
            }).ToArray();
            return child with { Chart = new(true, series) };
        }).ToArray();
    }
}

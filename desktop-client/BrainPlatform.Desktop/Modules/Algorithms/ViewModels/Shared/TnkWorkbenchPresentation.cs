using System.Globalization;
using BrainPlatform.Desktop.Shared.Mvvm;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

public sealed record TnkWorkbenchTab(string Title, string[] AlgorithmIds, bool IsStacked = false);
public sealed class TnkPlotPanel(string title, TnkResultChart initialChart, bool isStacked, string initialEmptyReason = "") : ObservableObject
{
    private TnkResultChart chart = initialChart;
    private string emptyReason = initialEmptyReason;
    public string Title { get; } = title;
    public bool IsStacked { get; } = isStacked;
    public TnkResultChart Chart { get => chart; private set => SetProperty(ref chart, value); }
    public string EmptyReason { get => emptyReason; private set => SetProperty(ref emptyReason, value); }

    internal void Update(TnkPlotPanel source)
    {
        if (Title != source.Title || IsStacked != source.IsStacked)
            throw new InvalidOperationException("Cannot replace a plot panel with a different chart structure.");
        Chart = source.Chart;
        EmptyReason = source.EmptyReason;
    }
}

/// <summary>Visual grouping and formatting only. Scientific values remain backend-owned.</summary>
public sealed partial class TnkDetailViewModel
{
    public IReadOnlyList<TnkWorkbenchTab> AnalysisTabs { get; } = [
        new("冥想趋势", ["tnk_composite_meditation", "tnk_relaxation", "tnk_spatial_distribution", "tnk_rhythm_stability"]),
        new("脑负荷指数", ["tnk_brainbeat", "tnk_brainbeat_flat"]),
        new("疲劳指数", ["tnk_fatigue"]), new("快慢波比", ["tnk_hai"]),
        new("RBP堆叠图", ["tnk_rbp"], true), new("皮质兴奋/抑制", ["tnk_aperiodic"]),
        new("额叶平衡", ["tnk_faa"]), new("个体Alpha峰", ["tnk_iapf"])];
    private int selectedTabIndex = 4;
    private IReadOnlyList<TnkPlotPanel> activePlots = [];
    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set
        {
            if (!SetProperty(ref selectedTabIndex, Math.Clamp(value, 0, AnalysisTabs.Count - 1))) return;
            RefreshActivePlots(forceReplace: true);
            NotifyPresentation();
        }
    }
    public IReadOnlyList<TnkPlotPanel> ActivePlots => activePlots;
    public static IReadOnlyList<TnkPlotPanel> BuildPlots(IReadOnlyList<TnkChildResultDisplay> children, TnkWorkbenchTab tab)
    {
        var selected = children.Where(child => tab.AlgorithmIds.Contains(child.AlgorithmId)).ToArray();
        if (tab.IsStacked)
        {
            var rbp = selected.FirstOrDefault();
            if (rbp is null) return [];
            return rbp.Chart.Series.GroupBy(series => series.Name.Split(" · ")[0]).Select(channel =>
                new TnkPlotPanel(channel.Key, new(rbp.Chart.IsTimeline, channel.OrderBy(series =>
                    Array.IndexOf(new[] { "delta", "theta", "alpha", "beta" }, series.Name.Split(" · ").Last())).Select(series => series with
                {
                    Name = series.Name.Split(" · ").Last(), Unit = "%",
                    Points = series.Points.Select(point => point with { Value = point.Value * 100 }).ToArray()
                }).ToArray()), true)).ToArray();
        }
        var series = selected.SelectMany(child => child.Chart.Series.Select(series => series with
        { Name = selected.Length > 1 ? $"{child.DisplayName.Replace("TNK ", "")} · {series.Name}" : series.Name.Split(" · ").Last() })).ToArray();
        var chart = new TnkResultChart(selected.FirstOrDefault()?.Chart.IsTimeline ?? false, series);
        var emptyReason = chart.HasValues ? "" : selected
            .Select(child => EmptyReason(child))
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason)) ?? "暂无有效结果；请查看右侧模块质量与失败原因。";
        return [new(tab.Title, chart, false, emptyReason)];
    }

    private static string EmptyReason(TnkChildResultDisplay child)
    {
        if (child.AlgorithmId == "tnk_faa")
            return "TNK 额叶 Alpha 不对称需要完整配对事件段；当前工作台没有事件段输入，不能进行动态游标计算。";
        return string.IsNullOrWhiteSpace(child.FailureText) ? "" : child.FailureText;
    }
    private void RefreshActivePlots(bool forceReplace = false)
    {
        var next = BuildPlots(ChildResults, AnalysisTabs[SelectedTabIndex]);
        var sameStructure = !forceReplace && activePlots.Count == next.Count &&
            activePlots.Zip(next).All(pair => HasSameChartStructure(pair.First, pair.Second));
        if (!sameStructure)
        {
            activePlots = next;
            RaisePropertyChanged(nameof(ActivePlots));
            return;
        }
        foreach (var pair in activePlots.Zip(next)) pair.First.Update(pair.Second);
    }
    private static bool HasSameChartStructure(TnkPlotPanel current, TnkPlotPanel next) =>
        current.Title == next.Title && current.IsStacked == next.IsStacked &&
        current.Chart.IsTimeline == next.Chart.IsTimeline &&
        current.Chart.Series.Select(series => (series.Name, series.Unit))
            .SequenceEqual(next.Chart.Series.Select(series => (series.Name, series.Unit)));
    internal double? ValueAtCursor(string id)
    {
        var child = ChildResults.FirstOrDefault(child => child.AlgorithmId == id);
        var series = child?.Chart.Series.FirstOrDefault();
        // Cadenced modules emit explicit backend "not due" points between calculations.
        // Keep the latest backend value through those points only; a computed unavailable
        // point remains unavailable. The displayed value keeps its backend timestamp.
        return SelectValueAtCursor(child?.Chart, WaveformPosition, hasSelectedTime);
    }

    internal static double? SelectValueAtCursor(TnkResultChart? chart, double positionSeconds, bool hasSelectedTime)
    {
        var series = chart?.Series.FirstOrDefault();
        return chart?.IsTimeline == true && hasSelectedTime
            ? series?.Points.LastOrDefault(point => point.X <= positionSeconds &&
                (!point.IsScheduledGap || point.Value.HasValue))?.Value
            : series?.Points.LastOrDefault()?.Value;
    }
    private TnkChartSeries? LastValidSeriesAtCursor(string id)
    {
        var series = ChildResults.FirstOrDefault(child => child.AlgorithmId == id)?.Chart.Series ?? [];
        return series.Select(candidate => new
            {
                Series = candidate,
                Point = hasSelectedTime
                    ? candidate.Points.LastOrDefault(point => point.X <= WaveformPosition && point.Value.HasValue)
                    : candidate.Points.LastOrDefault(point => point.Value.HasValue)
            })
            .Where(candidate => candidate.Point is not null)
            .OrderByDescending(candidate => candidate.Point!.X)
            .Select(candidate => candidate.Series)
            .FirstOrDefault();
    }
    private TnkChartPoint? LastValidPointAtCursor(string id) => LastValidSeriesAtCursor(id)?.Points.LastOrDefault(point =>
        point.Value.HasValue && (!hasSelectedTime || point.X <= WaveformPosition));
    private string MetricText(string id, string suffix = "") => ValueAtCursor(id) is double value
        ? value.ToString("0.##", CultureInfo.InvariantCulture) + suffix : "—";
    private string LastValidMetricText(string id, string suffix = "") => LastValidPointAtCursor(id)?.Value is double value
        ? value.ToString("0.##", CultureInfo.InvariantCulture) + suffix : "—";
    private string LastValidMetricStatus(string id)
    {
        var point = LastValidPointAtCursor(id);
        if (point is null) return "当前及此前暂无有效结果";
        return ValueAtCursor(id).HasValue
            ? $"当前游标对应结果 · {point.X:0.#} s"
            : $"当前窗口不可用 · 最近有效 {point.X:0.#} s";
    }
    public string CompositeValue => MetricText("tnk_composite_meditation");
    public double CompositeProgress => Math.Clamp(ValueAtCursor("tnk_composite_meditation") ?? 0, 0, 100);
    public string IapfValue => LastValidMetricText("tnk_iapf", " Hz");
    public string IapfValueStatus => LastValidMetricStatus("tnk_iapf");
    public string CorticalValue => LastValidMetricText("tnk_aperiodic");
    public string CorticalValueStatus => LastValidMetricStatus("tnk_aperiodic");
    public string CorticalSource => LastValidSeriesAtCursor("tnk_aperiodic")?.Name.Split(" · ").Last()
        ?? ChildResults.FirstOrDefault(child => child.AlgorithmId == "tnk_aperiodic")?.Chart.Series.FirstOrDefault()?.Name.Split(" · ").Last()
        ?? "等待来源";
    public string BrainbeatValue => MetricText("tnk_brainbeat");
    private static string Clock(double seconds) => $"{(int)(seconds / 60):00}:{(int)seconds % 60:00}";
    public string CurrentTimeText => Clock(WaveformPosition) + " / " + Clock(RecordingDuration);
    public string CompositeQuality => CompositeValue == "—" ? "当前窗口无有效分数" :
        ChildResults.FirstOrDefault(child => child.AlgorithmId == "tnk_composite_meditation")?.QualityText ?? "等待分析";
    public string RoleMappingText => string.Join(" · ", Catalog.TnkRoleChannels.Select(role => $"{role.Key} → {role.SelectedChannel}"));
    private void NotifyPresentation()
    {
        RefreshActivePlots();
        foreach (var name in new[] { nameof(CompositeValue), nameof(CompositeProgress), nameof(IapfValue),
            nameof(IapfValueStatus), nameof(IapfUpdateStatus), nameof(CorticalValue), nameof(CorticalValueStatus), nameof(CorticalSource), nameof(BrainbeatValue), nameof(CurrentTimeText), nameof(CompositeQuality), nameof(RoleMappingText) })
            RaisePropertyChanged(name);
    }
    private void NotifyCursorPresentation()
    {
        foreach (var name in new[] { nameof(CompositeValue), nameof(CompositeProgress), nameof(IapfValue), nameof(IapfValueStatus),
            nameof(CorticalValue), nameof(CorticalValueStatus), nameof(IapfUpdateStatus), nameof(BrainbeatValue), nameof(CompositeQuality), nameof(CurrentTimeText) }) RaisePropertyChanged(name);
    }
}

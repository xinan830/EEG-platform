using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed class ReviewTnkAnalysisViewModel : ObservableObject
{
    private static readonly IReadOnlyList<TnkWorkbenchTab> Tabs = [
        new("冥想趋势", ["tnk_composite_meditation", "tnk_relaxation", "tnk_spatial_distribution", "tnk_rhythm_stability"]),
        new("脑负荷指数", ["tnk_brainbeat", "tnk_brainbeat_flat"]),
        new("疲劳指数", ["tnk_fatigue"]), new("快慢波比", ["tnk_hai"]),
        new("RBP 堆叠图", ["tnk_rbp"], true), new("皮质兴奋/抑制", ["tnk_aperiodic"]),
        new("额叶平衡", ["tnk_faa"]), new("个体 Alpha 峰", ["tnk_iapf"]),
        new("ANT 时序指标", ["tnk_crimson_meditation", "tnk_zenlite_calmness", "tnk_zenlite_awareness", "tnk_zenlite_spectral_relaxation"])];
    private readonly TnkWindowChartHistory history = new();
    private IReadOnlyList<TnkChildResultDisplay> children = [];
    private IReadOnlyList<TnkPlotPanel> activePlots = [];
    private System.Text.Json.JsonElement outputSchema;
    private double cadenceSeconds = 1;
    private double cursorSeconds;
    private int selectedTabIndex;
    private string statusText = "等待 TNK 窗口结果。";
    private double visibleStartSeconds = double.NaN;

    public IReadOnlyList<TnkWorkbenchTab> AnalysisTabs => Tabs;
    public IReadOnlyList<TnkPlotPanel> ActivePlots => activePlots;
    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set
        {
            if (!SetProperty(ref selectedTabIndex, Math.Clamp(value, 0, Tabs.Count - 1))) return;
            RefreshPlots(force: true);
        }
    }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string CoverageText => history.CoverageText;
    public IReadOnlyList<TnkScoreState> ScoreStates { get; private set; } = [];
    public string CompositeValue => CompositePoint()?.Value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    public bool HasCompositeValue => CompositePoint()?.Value is not null;
    public double CompositeProgress => Math.Clamp(CompositePoint()?.Value ?? 0, 0, 100);
    public string CompositeQuality => HasCompositeValue ? Child("tnk_composite_meditation")?.QualityText ?? "可用" : "当前窗口无有效分数";
    public string ScoreStateStatus => $"有效累计 {FormatDuration(ScoreStates.Sum(state => state.DurationSeconds))}";
    public string IapfValue => RecentValue("tnk_iapf", " Hz");
    public string IapfValueStatus => RecentValueStatus("tnk_iapf");
    public string CorticalValue => RecentValue("tnk_aperiodic", "");
    public string CorticalValueStatus => RecentValueStatus("tnk_aperiodic");
    public string BrainbeatValue => CurrentValue("tnk_brainbeat")?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    public void Initialize(AlgorithmCatalogItem algorithm)
    {
        outputSchema = algorithm.OutputSchema.Clone();
        cadenceSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
    }

    public void SetVisibleStart(double seconds)
    {
        visibleStartSeconds = seconds;
        cursorSeconds = seconds;
        Clear();
    }

    public void SetCursor(double seconds)
    {
        if (!double.IsFinite(seconds) || Math.Floor(cursorSeconds) == Math.Floor(seconds)) return;
        cursorSeconds = seconds;
        RefreshSummary();
    }

    public void Clear()
    {
        history.Clear();
        children = [];
        activePlots = [];
        StatusText = "等待 TNK 窗口结果。";
        RaisePropertyChanged(nameof(ActivePlots));
        RaisePropertyChanged(nameof(CoverageText));
        RefreshSummary();
    }

    public void Apply(WindowPreviewResponse? response, double visibleStartSeconds)
    {
        if (double.IsFinite(visibleStartSeconds)) this.visibleStartSeconds = visibleStartSeconds;
        if (response is null)
        {
            Clear();
            return;
        }
        Apply(response.DynamicResponse, visibleStartSeconds);
        StatusText = response.Status switch
        {
            "available" => "TNK 窗口已更新。",
            "insufficient_data" => "当前 TNK 窗口数据不足。",
            "rejected" => "当前 TNK 窗口被质量规则拒绝。",
            "failed" or "unavailable" => "当前 TNK 窗口不可用。",
            _ => $"TNK 窗口状态：{response.Status}",
        };
    }

    public bool Apply(DynamicWindowResponse? response, double visibleStartSeconds)
    {
        if (double.IsFinite(visibleStartSeconds)) this.visibleStartSeconds = visibleStartSeconds;
        if (response is null || response.Result is null) return false;
        var projected = TnkResultChartProjection.ReadWindow(response, outputSchema);
        if (!history.Add(response, projected)) return false;
        children = history.Project(projected, cadenceSeconds);
        RefreshPlots();
        RefreshSummary();
        StatusText = history.CoverageText;
        RaisePropertyChanged(nameof(CoverageText));
        return true;
    }

    public void AddGap(TnkPlaybackGap gap, double visibleStartSeconds)
    {
        if (double.IsFinite(visibleStartSeconds)) this.visibleStartSeconds = visibleStartSeconds;
        history.AddGap(gap);
        children = history.Project(children, cadenceSeconds);
        RefreshPlots();
        RefreshSummary();
        StatusText = history.CoverageText;
        RaisePropertyChanged(nameof(CoverageText));
    }

    private void RefreshPlots(bool force = false)
    {
        var visible = double.IsFinite(this.visibleStartSeconds)
            ? children.Select(child => child with { Chart = child.Chart with
                { Series = child.Chart.Series.Select(series => series with
                    { Points = series.Points.Where(point => point.X + 1e-9 >= this.visibleStartSeconds).ToArray() }).ToArray() } }).ToArray()
            : children;
        var next = TnkDetailViewModel.BuildPlots(visible, Tabs[SelectedTabIndex]);
        if (!force && activePlots.Count == next.Count && activePlots.Zip(next).All(pair =>
                pair.First.Title == pair.Second.Title && pair.First.Chart.IsTimeline == pair.Second.Chart.IsTimeline &&
                pair.First.Chart.Series.Select(item => (item.Name, item.Unit))
                    .SequenceEqual(pair.Second.Chart.Series.Select(item => (item.Name, item.Unit)))))
        {
            foreach (var pair in activePlots.Zip(next)) pair.First.Update(pair.Second);
        }
        else
        {
            activePlots = next;
            RaisePropertyChanged(nameof(ActivePlots));
        }
    }

    private void RefreshSummary()
    {
        var series = Child("tnk_composite_meditation")?.Chart.Series.FirstOrDefault();
        if (series is not null && double.IsFinite(visibleStartSeconds))
            series = series with { Points = series.Points.Where(point => point.X >= visibleStartSeconds).ToArray() };
        var states = TnkOnDemandScoreStateProjection.Project(series, cursorSeconds);
        if (!ScoreStates.SequenceEqual(states)) ScoreStates = states;
        foreach (var property in new[]
        {
            nameof(ScoreStates), nameof(CompositeValue), nameof(HasCompositeValue), nameof(CompositeProgress),
            nameof(CompositeQuality), nameof(ScoreStateStatus), nameof(IapfValue), nameof(IapfValueStatus),
            nameof(CorticalValue), nameof(CorticalValueStatus), nameof(BrainbeatValue),
        }) RaisePropertyChanged(property);
    }

    private TnkChildResultDisplay? Child(string algorithmId) =>
        children.FirstOrDefault(child => child.AlgorithmId == algorithmId);

    private TnkChartPoint? CompositePoint() => Child("tnk_composite_meditation")?.Chart.Series.FirstOrDefault()?.Points
        .LastOrDefault(point => point.X <= cursorSeconds && (!point.IsScheduledGap || point.Value.HasValue));

    private double? CurrentValue(string algorithmId) =>
        TnkDetailViewModel.SelectValueAtCursor(Child(algorithmId)?.Chart, cursorSeconds, true);

    private string RecentValue(string algorithmId, string? suffix = null)
    {
        var point = RecentPoint(algorithmId);
        var series = Child(algorithmId)?.Chart.Series.FirstOrDefault();
        return point?.Value is double value
            ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                (suffix ?? (string.IsNullOrWhiteSpace(series?.Unit) ? "" : $" {series.Unit}"))
            : "—";
    }

    private string RecentValueStatus(string algorithmId)
    {
        var point = RecentPoint(algorithmId);
        if (point is null) return "暂无有效结果";
        return CurrentValue(algorithmId) is not null ? $"当前窗口 · {point.X:0.#} s" : $"最近有效 · {point.X:0.#} s";
    }

    private TnkChartPoint? RecentPoint(string algorithmId) => Child(algorithmId)?.Chart.Series
        .SelectMany(series => series.Points)
        .LastOrDefault(point => point.X >= visibleStartSeconds && point.X <= cursorSeconds && point.Value.HasValue);

    private static string FormatDuration(double seconds) =>
        $"{(int)(seconds / 60):00}:{(int)seconds % 60:00}";
}

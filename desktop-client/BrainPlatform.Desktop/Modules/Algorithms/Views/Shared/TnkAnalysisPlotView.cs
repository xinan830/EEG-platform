using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

/// <summary>ANT chart families rendered exclusively by SciChart from returned metric arrays.</summary>
public sealed class TnkAnalysisPlotView : UserControl
{
    private static readonly TimeSpan PlaybackCursorRefreshInterval = TimeSpan.FromMilliseconds(100);
    private readonly SciChartSurface surface = new() { Background = Brushes.White, ShowLicensingWarnings = false };
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new(2, 6, 0, 3) };
    private readonly WrapPanel legend = new() { Margin = new(0, 0, 0, 3) };
    private readonly TextBlock valuesAtCursor = new() { FontSize = 11, Foreground = Brushes.DimGray, Margin = new(2, 0, 0, 3) };
    private readonly VerticalLineAnnotation cursor = new() { Stroke = Brushes.SlateGray, StrokeThickness = 1, IsEditable = false };
    private readonly List<XyDataSeries<double, double>> dataSeries = [];
    private readonly List<IReadOnlyList<TnkChartPoint>> renderedPoints = [];
    private string? structureKey;
    private NumericAxis? xAxis;
    private NumericAxis? yAxis;
    private long lastPlaybackCursorRefresh;
    private long cursorDiagnosticsStarted;
    private int cursorRefreshCount;
    private int cursorRefreshSkippedCount;
    private bool refreshQueued;
    private readonly TnkPlotRefreshScheduler refreshScheduler;
    private TnkPlotPanel? subscribedPanel;
    private readonly TextBlock empty = new() { Text = "暂无有效结果 · 请查看质量状态", Foreground = Brushes.SlateGray,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    public static readonly DependencyProperty PanelProperty = DependencyProperty.Register(nameof(Panel), typeof(TnkPlotPanel),
        typeof(TnkAnalysisPlotView), new PropertyMetadata(null, (owner, args) =>
            ((TnkAnalysisPlotView)owner).OnPanelChanged(args.OldValue as TnkPlotPanel, args.NewValue as TnkPlotPanel)));
    public TnkPlotPanel? Panel { get => (TnkPlotPanel?)GetValue(PanelProperty); set => SetValue(PanelProperty, value); }
    public event EventHandler<double>? SeekRequested;
    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(nameof(PositionSeconds), typeof(double),
        typeof(TnkAnalysisPlotView), new PropertyMetadata(0d, (owner, _) => ((TnkAnalysisPlotView)owner).UpdateCursor()));
    public double PositionSeconds { get => (double)GetValue(PositionSecondsProperty); set => SetValue(PositionSecondsProperty, value); }

    public TnkAnalysisPlotView()
    {
        refreshScheduler = TnkPlotRefreshScheduler.For(Dispatcher);
        Loaded += (_, _) =>
        {
            ObservePanel(Panel);
            Refresh();
            UpdateCursor(force: true);
        };
        Unloaded += (_, _) =>
        {
            refreshQueued = false;
            ObservePanel(null);
        };
        IsVisibleChanged += (_, _) =>
        {
            ObservePanel(IsVisible ? Panel : null);
            if (IsVisible) ScheduleRefresh();
        };
        SciChart.Charting.ThemeManager.SetTheme(surface, "BrightSpark");
        surface.RenderPriority = RenderPriority.Low;
        surface.ChartModifier = new ModifierGroup(new MouseWheelZoomModifier(), new ZoomPanModifier(), new ZoomExtentsModifier(),
            new RolloverModifier { ShowTooltipOn = ShowTooltipOptions.MouseOver, DrawVerticalLine = true });
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        DockPanel.SetDock(legend, Dock.Top); root.Children.Add(legend);
        DockPanel.SetDock(valuesAtCursor, Dock.Top); root.Children.Add(valuesAtCursor);
        var chart = new Grid(); chart.Children.Add(surface); chart.Children.Add(empty); root.Children.Add(chart); Content = root;
        surface.MouseLeftButtonUp += (_, args) =>
        {
            if (Panel?.Chart.IsTimeline != true || !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) return;
            var time = surface.XAxis.GetCurrentCoordinateCalculator().GetDataValue(args.GetPosition((IInputElement)surface.GridLinesPanel).X);
            if (double.IsFinite(time)) SeekRequested?.Invoke(this, time);
        };
    }

    private void OnPanelChanged(TnkPlotPanel? previous, TnkPlotPanel? current)
    {
        ObservePanel(current);
        ScheduleRefresh();
    }

    private void ObservePanel(TnkPlotPanel? panel)
    {
        if (subscribedPanel is not null)
            subscribedPanel.PropertyChanged -= OnPanelPropertyChanged;
        subscribedPanel = null;
        if (IsLoaded && panel is not null)
        {
            panel.PropertyChanged += OnPanelPropertyChanged;
            subscribedPanel = panel;
        }
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TnkPlotPanel.Chart) or nameof(TnkPlotPanel.EmptyReason)) ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (!IsLoaded || !IsVisible || refreshQueued) return;
        refreshQueued = true;
        refreshScheduler.Request(ExecuteScheduledRefresh);
    }

    private void ExecuteScheduledRefresh()
    {
        refreshQueued = false;
        if (IsLoaded && IsVisible) Refresh();
    }

    private void Refresh()
    {
        if (!IsLoaded) return;
        var started = DesktopLog.PerformanceDiagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;
        var updateKind = "unchanged";
        if (Panel is not { } panel)
        {
            using (surface.SuspendUpdates())
                ResetStructure();
            updateKind = "reset";
        }
        else
        {
            empty.Text = string.IsNullOrWhiteSpace(panel.EmptyReason)
                ? "暂无有效结果 · 请查看质量状态"
                : panel.EmptyReason;
            title.Text = panel.Title;
            var targetHeight = panel.IsStacked ? 190 : 360;
            if (Height != targetHeight) Height = targetHeight;
            var key = StructureKey(panel);
            if (!string.Equals(structureKey, key, StringComparison.Ordinal))
            {
                using (surface.SuspendUpdates())
                    BuildStructure(panel, key);
                updateKind = "structure";
            }
            else
            {
                updateKind = UpdateSeries(panel);
            }
            var targetVisibility = panel.Chart.HasValues ? Visibility.Collapsed : Visibility.Visible;
            if (empty.Visibility != targetVisibility) empty.Visibility = targetVisibility;
            if (updateKind != "unchanged")
                UpdateCursor(force: true);
        }
        if (started != 0) RecordChartUpdate(started, updateKind);
    }

    private void RecordChartUpdate(long started, string updateKind)
    {
        var panel = Panel;
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var pointCount = panel?.Chart.Series.Sum(series => series.Points.Count) ?? 0;
        var latestWindowEnd = panel?.Chart.Series.SelectMany(series => series.Points)
            .Select(point => point.X).DefaultIfEmpty(double.NaN).Max() ?? double.NaN;
        DesktopLog.Info($"tnk_chart_update_perf scenario={DesktopLog.PerformanceScenario}" +
            $" panel={panel?.Title ?? "none"} update={updateKind} series={panel?.Chart.Series.Count ?? 0}" +
            $" points={pointCount.ToString(CultureInfo.InvariantCulture)}" +
            $" window_end_s={(double.IsFinite(latestWindowEnd) ? latestWindowEnd.ToString("0.######", CultureInfo.InvariantCulture) : "unknown")}" +
            $" elapsed_ms={elapsed.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    private static string StructureKey(TnkPlotPanel panel) =>
        $"{panel.Chart.IsTimeline}|{panel.IsStacked}|" + string.Join("|", panel.Chart.Series.Select((series, index) =>
            $"{series.Name}:{series.Unit}:{(panel.IsStacked && panel.Chart.IsTimeline ? "area" : "line")}"));

    private void BuildStructure(TnkPlotPanel panel, string key)
    {
        ResetStructure();
        structureKey = key;
        xAxis = Axis(panel.Chart.IsTimeline ? "记录时间 (s)" : "分段结果", AxisAlignment.Bottom);
        yAxis = Axis(panel.IsStacked ? "%" : panel.Chart.Series.FirstOrDefault()?.Unit ?? "", AxisAlignment.Left);
        var isPercentageChart = panel.Chart.Series.All(series => series.Unit == "%");
        if (isPercentageChart)
        {
            yAxis.AutoRange = AutoRange.Never;
            yAxis.VisibleRange = new DoubleRange(0, 100);
        }
        if (!panel.Chart.IsTimeline) xAxis.LabelProvider = new CategoryLabels(panel.Chart.Series);
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        BrainPlatform.Desktop.Shared.Waveform.RecordingTimeAxisPresentation.SetAxis(this, panel.Chart.IsTimeline ? xAxis : null);
        var index = 0;
        foreach (var item in panel.Chart.Series)
        {
            var color = SeriesColor(item.Name, index++);
            var values = new XyDataSeries<double, double> { SeriesName = $"{item.Name} ({item.Unit})" };
            AppendAll(values, item.Points);
            dataSeries.Add(values);
            renderedPoints.Add(item.Points.ToArray());
            BaseRenderableSeries line = panel.IsStacked && panel.Chart.IsTimeline
                ? new StackedMountainRenderableSeries { DataSeries = values, Fill = new SolidColorBrush(color), Stroke = color,
                    StackedGroupId = "bands", IsOneHundredPercent = false, DrawNaNAs = LineDrawMode.Gaps }
                : new FastLineRenderableSeries { DataSeries = values, Stroke = color, StrokeThickness = 2, DrawNaNAs = LineDrawMode.Gaps };
            surface.RenderableSeries.Add(line);
            if (panel.IsStacked)
            {
                legend.Children.Add(new TextBlock { Text = $"■ {item.Name}", Foreground = new SolidColorBrush(color), FontSize = 11, Margin = new(2, 0, 16, 0) });
                continue;
            }
            var toggle = new CheckBox { Content = item.Name, IsChecked = true, Foreground = new SolidColorBrush(color),
                FontSize = 11, Margin = new(2, 0, 16, 0) };
            toggle.Checked += (_, _) => line.IsVisible = true;
            toggle.Unchecked += (_, _) => line.IsVisible = false;
            legend.Children.Add(toggle);
        }
        if (panel.Chart.IsTimeline) surface.Annotations.Add(cursor);
        // Initial range only. Subsequent returned windows are appended without
        // rebuilding axes or resetting the operator's zoom/pan state.
        surface.ZoomExtents();
        // ZoomExtents can override the explicit percentage display bounds when
        // the first returned window contains only one valid band. RBP remains
        // a 0-100% display even when later windows fill the other bands.
        if (isPercentageChart) yAxis.VisibleRange = new DoubleRange(0, 100);
    }

    private string UpdateSeries(TnkPlotPanel panel)
    {
        if (panel.IsStacked)
        {
            return UpdateStackedSeries(panel);
        }
        var updated = false;
        var rebuilt = false;
        IDisposable? suspender = null;
        try
        {
            for (var index = 0; index < panel.Chart.Series.Count; index++)
            {
                var points = panel.Chart.Series[index].Points;
                var previous = renderedPoints[index];
                var appendStart = TnkChartAppendPlanner.FindAppendStart(previous, points);
                if (appendStart == -1) continue;
                suspender ??= surface.SuspendUpdates();
                updated = true;
                var oldEnd = previous.Count > 0 ? previous[^1].X : double.NaN;
                var followsLatest = xAxis?.VisibleRange is DoubleRange range && double.IsFinite(oldEnd) && range.Max >= oldEnd - .001;
                if (appendStart is int suffixStart)
                {
                    var suffix = points.Skip(suffixStart).ToArray();
                    AppendAll(dataSeries[index], suffix);
                    if (followsLatest && suffix.Length > 0 && xAxis?.VisibleRange is DoubleRange visible)
                        xAxis.VisibleRange = new DoubleRange(visible.Min, Math.Max(visible.Max, suffix[^1].X));
                }
                else
                {
                    rebuilt = true;
                    dataSeries[index].Clear();
                    AppendAll(dataSeries[index], points);
                }
                renderedPoints[index] = points.ToArray();
            }
            return !updated ? "unchanged" : rebuilt ? "line_rebuild" : "line_append";
        }
        finally
        {
            suspender?.Dispose();
        }
    }

    private string UpdateStackedSeries(TnkPlotPanel panel)
    {
        var previousEnd = renderedPoints.SelectMany(points => points).Select(point => point.X).DefaultIfEmpty(double.NaN).Max();
        var nextEnd = panel.Chart.Series.SelectMany(series => series.Points).Select(point => point.X).DefaultIfEmpty(double.NaN).Max();
        var hasChanges = false;
        var rebuilt = false;
        IDisposable? suspender = null;
        try
        {
            for (var index = 0; index < panel.Chart.Series.Count; index++)
            {
                var points = panel.Chart.Series[index].Points;
                var previous = renderedPoints[index];
                var appendStart = TnkChartAppendPlanner.FindAppendStart(previous, points);
                if (appendStart == -1) continue;
                suspender ??= surface.SuspendUpdates();
                if (appendStart is int suffixStart)
                    AppendAll(dataSeries[index], points.Skip(suffixStart).ToArray());
                else
                {
                    rebuilt = true;
                    // A late recovered window or bounded-history trim changed the prefix.
                    // Rebuild only for this explicit reconciliation/reset case.
                    var replacement = new XyDataSeries<double, double> { SeriesName = dataSeries[index].SeriesName };
                    AppendAll(replacement, points);
                    dataSeries[index] = replacement;
                    surface.RenderableSeries[index].DataSeries = replacement;
                }
                renderedPoints[index] = points.ToArray();
                hasChanges = true;
            }
            if (!hasChanges) return "unchanged";
            var advancedRange = TnkTimelineRange.AdvanceIfFollowing(xAxis?.VisibleRange as DoubleRange, previousEnd, nextEnd);
            if (advancedRange is not null) xAxis!.VisibleRange = advancedRange;
            return rebuilt ? "stacked_rebuild" : "stacked_append";
        }
        finally
        {
            suspender?.Dispose();
        }
    }

    private static void AppendAll(XyDataSeries<double, double> target, IReadOnlyList<TnkChartPoint> points)
    {
        if (points.Count == 0) return;
        target.Append(points.Select(point => point.X), points.Select(point => point.Value ?? double.NaN));
    }

    private void ResetStructure()
    {
        surface.RenderableSeries.Clear(); surface.XAxes.Clear(); surface.YAxes.Clear(); legend.Children.Clear();
        surface.Annotations.Clear(); dataSeries.Clear(); renderedPoints.Clear(); structureKey = null;
        xAxis = null; yAxis = null;
    }

    private void UpdateCursor(bool force = false)
    {
        if (!IsLoaded || !IsVisible || Panel is not { } panel) return;
        if (!force && !CanRefreshPlaybackCursor()) return;

        lastPlaybackCursorRefresh = Stopwatch.GetTimestamp();
        if (DesktopLog.PerformanceDiagnosticsEnabled) cursorRefreshCount++;
        cursor.X1 = PositionSeconds;
        if (panel.Chart.IsTimeline && xAxis?.VisibleRange is DoubleRange visible)
        {
            var expanded = ExpandTimelineRange(visible, PositionSeconds);
            if (expanded is not null) xAxis.VisibleRange = expanded;
        }
        valuesAtCursor.Text = string.Join("  |  ", panel.Chart.Series.Select(series =>
        {
            var point = panel.Chart.IsTimeline ? series.Points.LastOrDefault(point => point.X <= PositionSeconds) : series.Points.LastOrDefault();
            var value = point?.Value is double number ? $"{number:0.##} {series.Unit}"
                : point is null && series.Points.Any(candidate => candidate.Value.HasValue && candidate.X > PositionSeconds)
                    ? "尚未到首个有效窗口"
                    : point is null ? "—" : "当前窗口不可用";
            return $"{series.Name}: {value}";
        }));
        if (panel.Chart.Series.Any() && panel.Chart.Series.All(series => series.Points.Count <= 1))
            valuesAtCursor.Text += "  · 仅一个已返回窗口；更多窗口返回后形成趋势";

        RecordCursorRefreshDiagnostics();
    }

    internal static DoubleRange? ExpandTimelineRange(DoubleRange? visible, double positionSeconds)
    {
        if (visible is null || !double.IsFinite(positionSeconds) ||
            (positionSeconds >= visible.Min && positionSeconds <= visible.Max)) return null;
        return new DoubleRange(Math.Min(visible.Min, positionSeconds), Math.Max(visible.Max, positionSeconds));
    }

    private bool CanRefreshPlaybackCursor()
    {
        var now = Stopwatch.GetTimestamp();
        if (DesktopLog.PerformanceDiagnosticsEnabled && cursorDiagnosticsStarted == 0) cursorDiagnosticsStarted = now;
        if (lastPlaybackCursorRefresh == 0 || Stopwatch.GetElapsedTime(lastPlaybackCursorRefresh, now) >= PlaybackCursorRefreshInterval)
            return true;
        if (DesktopLog.PerformanceDiagnosticsEnabled)
        {
            cursorRefreshSkippedCount++;
            RecordCursorRefreshDiagnostics(now);
        }
        return false;
    }

    private void RecordCursorRefreshDiagnostics(long now = 0)
    {
        if (!DesktopLog.PerformanceDiagnosticsEnabled) return;
        now = now == 0 ? Stopwatch.GetTimestamp() : now;
        if (cursorDiagnosticsStarted == 0) cursorDiagnosticsStarted = now;
        if (Stopwatch.GetElapsedTime(cursorDiagnosticsStarted, now) < TimeSpan.FromSeconds(5)) return;

        DesktopLog.Info($"tnk_chart_cursor_perf scenario={DesktopLog.PerformanceScenario}" +
            $" panel={Panel?.Title ?? "none"} refreshed={cursorRefreshCount} throttled={cursorRefreshSkippedCount}" +
            $" interval_ms={PlaybackCursorRefreshInterval.TotalMilliseconds:0}");
        cursorDiagnosticsStarted = now;
        cursorRefreshCount = cursorRefreshSkippedCount = 0;
    }

    private sealed class CategoryLabels(IReadOnlyList<TnkChartSeries> series) : SciChart.Charting.Visuals.Axes.LabelProviders.NumericLabelProvider
    {
        public override string FormatLabel(IComparable value)
        {
            var x = Convert.ToDouble(value);
            return series.SelectMany(item => item.Points).FirstOrDefault(point => Math.Abs(point.X - x) < .001)?.Label ?? "";
        }
    }

    private static NumericAxis Axis(string title, AxisAlignment alignment) => new() { AxisTitle = title, AxisAlignment = alignment,
        AutoRange = AutoRange.Once, GrowBy = new DoubleRange(.03, .05), DrawMajorBands = false, DrawMinorTicks = false,
        DrawMinorGridLines = false, DrawMajorGridLines = false, FontSize = 10 };
    private static Color SeriesColor(string name, int index)
    {
        if (name == "delta") return (Color)ColorConverter.ConvertFromString("#E45756");
        if (name == "theta") return (Color)ColorConverter.ConvertFromString("#F2C94C");
        if (name == "alpha") return (Color)ColorConverter.ConvertFromString("#54A24B");
        if (name == "beta") return (Color)ColorConverter.ConvertFromString("#4C78A8");
        Color[] colors = [Colors.SeaGreen, Colors.MediumPurple, Colors.CornflowerBlue, Colors.DarkOrange, Colors.IndianRed];
        return colors[index % colors.Length];
    }
}

internal static class TnkTimelineRange
{
    public static DoubleRange? AdvanceIfFollowing(DoubleRange? visible, double previousEnd, double nextEnd)
    {
        if (visible is null || !double.IsFinite(previousEnd) || !double.IsFinite(nextEnd)
            || visible.Max < previousEnd - .001 || nextEnd <= visible.Max)
            return null;
        var shift = nextEnd - visible.Max;
        return new DoubleRange(visible.Min + shift, nextEnd);
    }
}

internal static class TnkChartAppendPlanner
{
    /// <returns>-1 for unchanged data, the suffix offset for an append, or null when the prefix changed.</returns>
    internal static int? FindAppendStart(IReadOnlyList<TnkChartPoint> previous, IReadOnlyList<TnkChartPoint> next)
    {
        if (previous.Count == next.Count && previous.SequenceEqual(next)) return -1;
        if (next.Count < previous.Count) return null;
        for (var index = 0; index < previous.Count; index++)
            if (previous[index] != next[index]) return null;
        return previous.Count;
    }
}

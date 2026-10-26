using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        SciChart.Charting.ThemeManager.SetTheme(surface, "BrightSpark");
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
        if (previous is not null) previous.PropertyChanged -= OnPanelPropertyChanged;
        if (current is not null) current.PropertyChanged += OnPanelPropertyChanged;
        Refresh();
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TnkPlotPanel.Chart) or nameof(TnkPlotPanel.EmptyReason)) Refresh();
    }

    private void Refresh()
    {
        using (surface.SuspendUpdates())
        {
            if (Panel is not { } panel)
            {
                ResetStructure();
                return;
            }
            empty.Text = string.IsNullOrWhiteSpace(panel.EmptyReason)
                ? "暂无有效结果 · 请查看质量状态"
                : panel.EmptyReason;
            title.Text = panel.Title;
            Height = panel.IsStacked ? 190 : 360;
            var key = StructureKey(panel);
            if (!string.Equals(structureKey, key, StringComparison.Ordinal)) BuildStructure(panel, key);
            else UpdateSeries(panel);
            empty.Visibility = panel.Chart.HasValues ? Visibility.Collapsed : Visibility.Visible;
            UpdateCursor();
        }
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

    private void UpdateSeries(TnkPlotPanel panel)
    {
        if (panel.IsStacked)
        {
            UpdateStackedSeries(panel);
            return;
        }
        for (var index = 0; index < panel.Chart.Series.Count; index++)
        {
            var points = panel.Chart.Series[index].Points;
            var previous = renderedPoints[index];
            if (previous.SequenceEqual(points)) continue;
            var appending = points.Count >= previous.Count && previous.SequenceEqual(points.Take(previous.Count));
            var oldEnd = previous.Count > 0 ? previous[^1].X : double.NaN;
            var followsLatest = xAxis?.VisibleRange is DoubleRange range && double.IsFinite(oldEnd) && range.Max >= oldEnd - .001;
            if (appending)
            {
                var suffix = points.Skip(previous.Count).ToArray();
                AppendAll(dataSeries[index], suffix);
                if (followsLatest && suffix.Length > 0 && xAxis?.VisibleRange is DoubleRange visible)
                    xAxis.VisibleRange = new DoubleRange(visible.Min, Math.Max(visible.Max, suffix[^1].X));
            }
            else
            {
                dataSeries[index].Clear();
                AppendAll(dataSeries[index], points);
            }
            renderedPoints[index] = points.ToArray();
        }
    }

    private void UpdateStackedSeries(TnkPlotPanel panel)
    {
        var previousEnd = renderedPoints.SelectMany(points => points).Select(point => point.X).DefaultIfEmpty(double.NaN).Max();
        var nextEnd = panel.Chart.Series.SelectMany(series => series.Points).Select(point => point.X).DefaultIfEmpty(double.NaN).Max();
        var hasChanges = false;
        for (var index = 0; index < panel.Chart.Series.Count; index++)
        {
            var points = panel.Chart.Series[index].Points;
            if (renderedPoints[index].SequenceEqual(points)) continue;
            var replacement = new XyDataSeries<double, double> { SeriesName = dataSeries[index].SeriesName };
            AppendAll(replacement, points);
            dataSeries[index] = replacement;
            surface.RenderableSeries[index].DataSeries = replacement;
            renderedPoints[index] = points.ToArray();
            hasChanges = true;
        }
        if (!hasChanges) return;
        var advancedRange = TnkTimelineRange.AdvanceIfFollowing(xAxis?.VisibleRange as DoubleRange, previousEnd, nextEnd);
        if (advancedRange is not null) xAxis!.VisibleRange = advancedRange;
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

    private void UpdateCursor()
    {
        if (Panel is not { } panel) return;
        cursor.X1 = PositionSeconds;
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

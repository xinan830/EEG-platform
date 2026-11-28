using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using SciChart.Charting;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.PointMarkers;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

public partial class LiveScalarTrendChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new()
    {
        AxisTitle = "采集时间（s）",
        AutoRange = AutoRange.Never,
        CursorTextFormatting = "0.## 's'",
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left,
        AutoRange = AutoRange.Always,
        GrowBy = new DoubleRange(0.12, 0.12),
        CursorTextFormatting = "0.##",
    };
    private readonly BoxAnnotation classicAlphaBand = ScalarTrendChartStyle.CreateClassicAlphaBand();
    private readonly XyDataSeries<double, double> values = new();
    private readonly XyDataSeries<double, double> cogSeries = new() { SeriesName = "COG 重心 (Hz)" };
    private readonly XyDataSeries<double, double> peakMarkerSeries = new();
    private readonly XyDataSeries<double, double> cogMarkerSeries = new();
    private readonly FastLineRenderableSeries cogLineSeries;
    private readonly FastLineRenderableSeries mainLineSeries;
    private readonly XyScatterRenderableSeries iapfPeakMarkers;
    private readonly XyScatterRenderableSeries iapfCogMarkers;
    private readonly EllipsePointMarker defaultMarker = ScalarTrendChartStyle.CreateDefaultPointMarker();
    private readonly IncrementalTrendSeries<LiveScalarPoint> trend;
    private INotifyCollectionChanged? observedPoints;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(LiveScalarTrendChart),
        new PropertyMetadata(null, OnPointsChanged));

    public static readonly DependencyProperty StartSecondsProperty = DependencyProperty.Register(
        nameof(StartSeconds), typeof(double), typeof(LiveScalarTrendChart),
        new PropertyMetadata(0d, OnDataChanged));

    public static readonly DependencyProperty EndSecondsProperty = DependencyProperty.Register(
        nameof(EndSeconds), typeof(double), typeof(LiveScalarTrendChart),
        new PropertyMetadata(1d, OnDataChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LiveScalarTrendChart),
        new PropertyMetadata("", OnDataChanged));

    public static readonly DependencyProperty AlgorithmIdProperty = DependencyProperty.Register(
        nameof(AlgorithmId), typeof(string), typeof(LiveScalarTrendChart),
        new PropertyMetadata("", OnDataChanged));

    public static readonly DependencyProperty IapfDisplayModeProperty = DependencyProperty.Register(
        nameof(IapfDisplayMode), typeof(string), typeof(LiveScalarTrendChart),
        new PropertyMetadata(ScalarTrendChartStyle.IapfDisplayModeCog, OnDataChanged));

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double StartSeconds
    {
        get => (double)GetValue(StartSecondsProperty);
        set => SetValue(StartSecondsProperty, value);
    }

    public double EndSeconds
    {
        get => (double)GetValue(EndSecondsProperty);
        set => SetValue(EndSecondsProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string AlgorithmId
    {
        get => (string)GetValue(AlgorithmIdProperty);
        set => SetValue(AlgorithmIdProperty, value);
    }

    public string IapfDisplayMode
    {
        get => (string)GetValue(IapfDisplayModeProperty);
        set => SetValue(IapfDisplayModeProperty, value);
    }

    public LiveScalarTrendChart()
    {
        InitializeComponent();
        trend = new(values, point => (point.TimeSeconds,
            point.State == "Complete" && point.Value is { } v && double.IsFinite(v) ? v : double.NaN));
        cogLineSeries = ScalarTrendChartStyle.CreateCogReferenceSeries(cogSeries);
        mainLineSeries = ScalarTrendChartStyle.CreateMainLineSeries(values);
        iapfPeakMarkers = ScalarTrendChartStyle.CreateIapfPeakScatterSeries(peakMarkerSeries);
        iapfCogMarkers = ScalarTrendChartStyle.CreateIapfCogScatterSeries(cogMarkerSeries);
        ScalarTrendChartStyle.ApplySurfaceChrome(surface);
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        ScalarTrendChartStyle.ApplyAxisStyle(xAxis);
        ScalarTrendChartStyle.ApplyAxisStyle(yAxis);
        surface.Annotations.Add(classicAlphaBand);
        surface.RenderableSeries.Add(cogLineSeries);
        surface.RenderableSeries.Add(mainLineSeries);
        surface.RenderableSeries.Add(iapfPeakMarkers);
        surface.RenderableSeries.Add(iapfCogMarkers);
        surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true,
            DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
            ReceiveHandledEvents = true,
        };
        ((Grid)Content).Children.Insert(0, surface);
        UpdateSeries();
    }

    internal static (bool AutoRange, double Min, double Max) ComputeYAxisRange(string algorithmId) =>
        ScalarTrendChartStyle.ComputeYAxisRange(algorithmId);

    private static void OnPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var chart = (LiveScalarTrendChart)sender;
        if (chart.observedPoints is not null)
            chart.observedPoints.CollectionChanged -= chart.OnPointsCollectionChanged;
        chart.observedPoints = args.NewValue as INotifyCollectionChanged;
        if (chart.observedPoints is not null)
            chart.observedPoints.CollectionChanged += chart.OnPointsCollectionChanged;
        chart.UpdateSeries();
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((LiveScalarTrendChart)sender).UpdateSeries();

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSeries();

    private void UpdateSeries()
    {
        var items = Points?.Cast<LiveScalarPoint>().ToArray() ?? [];
        var isIapf = string.Equals(AlgorithmId, "iapf", StringComparison.OrdinalIgnoreCase);
        var (autoRange, min, max) = ComputeYAxisRange(AlgorithmId);
        if (autoRange)
        {
            yAxis.AutoRange = AutoRange.Always;
            yAxis.GrowBy = new DoubleRange(0.12, 0.12);
        }
        else
        {
            yAxis.AutoRange = AutoRange.Never;
            yAxis.VisibleRange = new DoubleRange(min, max);
        }

        classicAlphaBand.IsHidden = !isIapf;
        var axisUnit = ScalarTrendChartStyle.FormatDisplayUnit(Unit);
        yAxis.AxisTitle = axisUnit;
        values.SeriesName = isIapf ? "IAPF (Hz)" : (string.IsNullOrEmpty(axisUnit) ? AlgorithmId : axisUnit);
        if (isIapf)
        {
            var mode = ScalarTrendChartStyle.NormalizeIapfDisplayMode(IapfDisplayMode);
            mainLineSeries.PointMarker = null;
            var ordered = items.OrderBy(p => p.TimeSeconds).ToArray();
            peakMarkerSeries.SeriesName = "瞬时峰值 (Hz)";
            double? ResolveCog(LiveScalarPoint p) =>
                p.CogHz is { } c && double.IsFinite(c) ? c
                : (p.State == "Complete" && string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase) && p.Value is { } v && double.IsFinite(v) ? v : null);
            var hasCog = ordered.Any(p => ResolveCog(p) is not null);
            ScalarTrendChartStyle.RebuildSeries(cogSeries, ordered.Select(p =>
                (p.TimeSeconds, ResolveCog(p) ?? double.NaN)));

            if (mode == ScalarTrendChartStyle.IapfDisplayModeCog)
            {
                ScalarTrendChartStyle.ApplyCogSeriesStyle(cogLineSeries, isPrimary: true);
                cogLineSeries.IsVisible = hasCog;
                mainLineSeries.IsVisible = false;
                iapfPeakMarkers.IsVisible = false;
                iapfCogMarkers.IsVisible = false;
                ScalarTrendChartStyle.RebuildSeries(values, []);
                ScalarTrendChartStyle.RebuildSeries(peakMarkerSeries, []);
                ScalarTrendChartStyle.RebuildSeries(cogMarkerSeries, []);
            }
            else if (mode == ScalarTrendChartStyle.IapfDisplayModeCompare)
            {
                ScalarTrendChartStyle.ApplyCogSeriesStyle(cogLineSeries, isPrimary: true);
                ScalarTrendChartStyle.ApplyIapfPeakScatterStyle(iapfPeakMarkers, isCompareMode: true);
                cogLineSeries.IsVisible = hasCog;
                mainLineSeries.IsVisible = false;
                iapfPeakMarkers.IsVisible = true;
                iapfCogMarkers.IsVisible = true;
                ScalarTrendChartStyle.RebuildSeries(values, []);
                ScalarTrendChartStyle.RebuildSeries(peakMarkerSeries, ordered
                    .Where(p => p.State == "Complete" && p.Value is { } v && double.IsFinite(v) &&
                        !string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (p.TimeSeconds, p.Value!.Value)));
                ScalarTrendChartStyle.RebuildSeries(cogMarkerSeries, ordered
                    .Where(p => p.State == "Complete" && p.Value is { } v && double.IsFinite(v) &&
                        string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (p.TimeSeconds, p.Value!.Value)));
            }
            else
            {
                ScalarTrendChartStyle.ApplyCogSeriesStyle(cogLineSeries, isPrimary: false);
                ScalarTrendChartStyle.ApplyIapfPeakScatterStyle(iapfPeakMarkers, isCompareMode: false);
                cogLineSeries.IsVisible = hasCog;
                mainLineSeries.IsVisible = true;
                iapfPeakMarkers.IsVisible = true;
                iapfCogMarkers.IsVisible = true;
                ScalarTrendChartStyle.RebuildSeries(values, ordered.Select(p =>
                    (p.TimeSeconds,
                        p.State == "Complete" && p.Value is { } v && double.IsFinite(v) &&
                        !string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase)
                            ? v
                            : double.NaN)));
                ScalarTrendChartStyle.RebuildSeries(peakMarkerSeries, ordered
                    .Where(p => p.State == "Complete" && p.Value is { } v && double.IsFinite(v) &&
                        !string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (p.TimeSeconds, p.Value!.Value)));
                ScalarTrendChartStyle.RebuildSeries(cogMarkerSeries, ordered
                    .Where(p => p.State == "Complete" && p.Value is { } v && double.IsFinite(v) &&
                        string.Equals(p.EstimateSource, "cog", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (p.TimeSeconds, p.Value!.Value)));
            }
        }
        else
        {
            trend.Update(items);
            mainLineSeries.IsVisible = true;
            mainLineSeries.PointMarker = defaultMarker;
            cogLineSeries.IsVisible = false;
            iapfPeakMarkers.IsVisible = false;
            iapfCogMarkers.IsVisible = false;
            ScalarTrendChartStyle.RebuildSeries(cogSeries, []);
            ScalarTrendChartStyle.RebuildSeries(peakMarkerSeries, []);
            ScalarTrendChartStyle.RebuildSeries(cogMarkerSeries, []);
        }

        xAxis.VisibleRange = new DoubleRange(StartSeconds, Math.Max(StartSeconds + 1d, EndSeconds));
        EmptyState.Visibility = items.Any(point => point.State == "Complete" && (point.Value is not null || point.CogHz is not null))
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}

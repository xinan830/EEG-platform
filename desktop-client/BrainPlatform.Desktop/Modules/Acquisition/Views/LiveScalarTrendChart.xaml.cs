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
using SciChart.Charting.Visuals.Axes;
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
        DrawMajorBands = false,
        DrawMinorGridLines = false,
        DrawMinorTicks = false,
        FontSize = 11,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        CursorTextFormatting = "0.## 's'",
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left,
        AutoRange = AutoRange.Always,
        GrowBy = new DoubleRange(0.12, 0.12),
        DrawMajorBands = false,
        DrawMinorGridLines = false,
        DrawMinorTicks = false,
        FontSize = 11,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        CursorTextFormatting = "0.##",
    };
    private readonly XyDataSeries<double, double> values = new();
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

    public LiveScalarTrendChart()
    {
        InitializeComponent();
        trend = new(values, point => (point.TimeSeconds,
            point.State == "Complete" && point.Value is { } v && double.IsFinite(v) ? v : double.NaN));
        ThemeManager.SetTheme(surface, "BrightSpark");
        surface.Background = Brushes.White;
        surface.BorderThickness = new Thickness(0);
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            DataSeries = values,
            Stroke = Color.FromRgb(22, 119, 155),
            StrokeThickness = 2,
            DrawNaNAs = LineDrawMode.Gaps,
        });
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

    internal static (bool AutoRange, double Min, double Max) ComputeYAxisRange(string algorithmId)
    {
        if (!string.Equals(algorithmId, "iapf", StringComparison.OrdinalIgnoreCase))
            return (true, double.NaN, double.NaN);

        return (false, 8.0, 13.0);
    }

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

        var axisUnit = string.Equals(Unit, "dimensionless", StringComparison.OrdinalIgnoreCase) ? "" : Unit;
        yAxis.AxisTitle = axisUnit;
        values.SeriesName = string.IsNullOrEmpty(axisUnit) ? AlgorithmId : axisUnit;
        trend.Update(items);
        xAxis.VisibleRange = new DoubleRange(StartSeconds, Math.Max(StartSeconds + 1d, EndSeconds));
        EmptyState.Visibility = items.Any(point => point.State == "Complete" && point.Value is not null)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}

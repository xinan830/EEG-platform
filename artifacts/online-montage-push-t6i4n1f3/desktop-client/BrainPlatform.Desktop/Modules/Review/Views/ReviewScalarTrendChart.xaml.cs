using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public partial class ReviewScalarTrendChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new() { AxisTitle = "记录时间（s）", AutoRange = AutoRange.Never, DrawMajorBands = false, DrawMinorGridLines = false };
    private readonly NumericAxis yAxis = new() { AxisAlignment = AxisAlignment.Left, AutoRange = AutoRange.Always, GrowBy = new DoubleRange(0.12, 0.12), DrawMajorBands = false, DrawMinorGridLines = false };
    private readonly XyDataSeries<double, double> values = new();
    private readonly IncrementalTrendSeries<ReviewScalarPoint> trend;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IEnumerable), typeof(ReviewScalarTrendChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty StartSecondsProperty = DependencyProperty.Register(nameof(StartSeconds), typeof(double), typeof(ReviewScalarTrendChart), new PropertyMetadata(0d, OnDataChanged));
    public static readonly DependencyProperty EndSecondsProperty = DependencyProperty.Register(nameof(EndSeconds), typeof(double), typeof(ReviewScalarTrendChart), new PropertyMetadata(1d, OnDataChanged));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(ReviewScalarTrendChart), new PropertyMetadata("", OnDataChanged));
    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double StartSeconds { get => (double)GetValue(StartSecondsProperty); set => SetValue(StartSecondsProperty, value); }
    public double EndSeconds { get => (double)GetValue(EndSecondsProperty); set => SetValue(EndSecondsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public ReviewScalarTrendChart()
    {
        InitializeComponent();
        trend = new(values, point => (point.TimeSeconds,
            point.State is "Complete" or "Partial" ? point.Value ?? double.NaN : double.NaN));
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        RecordingTimeAxisPresentation.SetAxis(this, xAxis);
        surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            DataSeries = values, Stroke = Color.FromRgb(22, 119, 155), StrokeThickness = 2,
            DrawNaNAs = LineDrawMode.Gaps,
        });
        surface.ChartModifier = new RolloverModifier { IsEnabled = true, DrawVerticalLine = true, ShowTooltipOn = ShowTooltipOptions.MouseOver };
        ((Grid)Content).Children.Insert(0, surface);
        UpdateSeries();
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ReviewScalarTrendChart)sender).UpdateSeries();

    private void UpdateSeries()
    {
        yAxis.AxisTitle = Unit;
        values.SeriesName = Unit;
        var items = Points?.Cast<ReviewScalarPoint>().ToArray() ?? [];
        trend.Update(items);
        xAxis.VisibleRange = new DoubleRange(StartSeconds, Math.Max(StartSeconds + 1, EndSeconds));
        EmptyState.Visibility = items.Any(point => point.Value is not null && point.State is "Complete" or "Partial")
            ? Visibility.Collapsed : Visibility.Visible;
    }
}

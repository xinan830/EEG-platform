using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.PeakFrequency;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.PointMarkers;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.PeakFrequency;

public partial class PeakFrequencyTrendChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new()
    {
        AxisTitle = "记录时间（s）", AutoRange = AutoRange.Never,
        DrawMajorBands = false, DrawMinorGridLines = false, FontSize = 12,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left, AxisTitle = "峰频率（Hz）",
        AutoRange = AutoRange.Always, GrowBy = new DoubleRange(0.12, 0.12),
        DrawMajorBands = false, DrawMinorGridLines = false, FontSize = 12,
    };
    private readonly XyDataSeries<double, double> values = new() { SeriesName = "峰频率（Hz）" };

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(PeakFrequencyTrendChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty StartSecondsProperty = DependencyProperty.Register(
        nameof(StartSeconds), typeof(double), typeof(PeakFrequencyTrendChart), new PropertyMetadata(0d, OnDataChanged));
    public static readonly DependencyProperty EndSecondsProperty = DependencyProperty.Register(
        nameof(EndSeconds), typeof(double), typeof(PeakFrequencyTrendChart), new PropertyMetadata(1d, OnDataChanged));

    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double StartSeconds { get => (double)GetValue(StartSecondsProperty); set => SetValue(StartSecondsProperty, value); }
    public double EndSeconds { get => (double)GetValue(EndSecondsProperty); set => SetValue(EndSecondsProperty, value); }

    public PeakFrequencyTrendChart()
    {
        InitializeComponent();
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            DataSeries = values,
            Stroke = Color.FromRgb(19, 124, 109),
            StrokeThickness = 2,
            DrawNaNAs = LineDrawMode.Gaps,
            PointMarker = new EllipsePointMarker { Width = 7, Height = 7, Fill = Color.FromRgb(19, 124, 109) },
        });
        surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true, DrawVerticalLine = true, ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
        ((Grid)Content).Children.Insert(0, surface);
        UpdateSeries();
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PeakFrequencyTrendChart)sender).UpdateSeries();

    private void UpdateSeries()
    {
        values.Clear();
        var points = Points?.Cast<PeakFrequencyResultPoint>().ToArray() ?? [];
        foreach (var point in points)
            values.Append(point.TimeSeconds, point.IsComplete ? point.ValueHz!.Value : double.NaN);
        xAxis.VisibleRange = new DoubleRange(StartSeconds, Math.Max(StartSeconds + 1, EndSeconds));
        EmptyState.Visibility = points.Any(point => point.IsComplete) ? Visibility.Collapsed : Visibility.Visible;
    }
}

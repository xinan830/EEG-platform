using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.ThetaBeta;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.ThetaBeta;

public partial class ThetaBetaTrendChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new() { AxisTitle = "记录时间（s）", AutoRange = AutoRange.Never, DrawMajorBands = false, DrawMinorGridLines = false, FontSize = 12 };
    private readonly NumericAxis yAxis = new() { AxisAlignment = AxisAlignment.Left, AxisTitle = "Theta/Beta（无量纲）", AutoRange = AutoRange.Always, GrowBy = new DoubleRange(0.12, 0.12), DrawMajorBands = false, DrawMinorGridLines = false, FontSize = 12 };
    private readonly XyDataSeries<double, double> values = new() { SeriesName = "Theta/Beta" };

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IEnumerable), typeof(ThetaBetaTrendChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty StartSecondsProperty = DependencyProperty.Register(nameof(StartSeconds), typeof(double), typeof(ThetaBetaTrendChart), new PropertyMetadata(0d, OnDataChanged));
    public static readonly DependencyProperty EndSecondsProperty = DependencyProperty.Register(nameof(EndSeconds), typeof(double), typeof(ThetaBetaTrendChart), new PropertyMetadata(1d, OnDataChanged));
    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double StartSeconds { get => (double)GetValue(StartSecondsProperty); set => SetValue(StartSecondsProperty, value); }
    public double EndSeconds { get => (double)GetValue(EndSecondsProperty); set => SetValue(EndSecondsProperty, value); }

    public ThetaBetaTrendChart()
    {
        InitializeComponent();
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.RenderableSeries.Add(new FastLineRenderableSeries { DataSeries = values, Stroke = Color.FromRgb(22, 119, 155), StrokeThickness = 2, DrawNaNAs = LineDrawMode.Gaps });
        surface.ChartModifier = new RolloverModifier { IsEnabled = true, DrawVerticalLine = true, ShowTooltipOn = ShowTooltipOptions.MouseOver };
        ((Grid)Content).Children.Insert(0, surface);
        UpdateSeries();
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ThetaBetaTrendChart)sender).UpdateSeries();

    private void UpdateSeries()
    {
        values.Clear();
        var points = Points?.Cast<ThetaBetaResultPoint>().ToArray() ?? [];
        foreach (var point in points) values.Append(point.TimeSeconds, point.IsComplete ? point.Value!.Value : double.NaN);
        xAxis.VisibleRange = new DoubleRange(StartSeconds, Math.Max(StartSeconds + 1, EndSeconds));
        EmptyState.Visibility = points.Any(point => point.IsComplete) ? Visibility.Collapsed : Visibility.Visible;
    }
}

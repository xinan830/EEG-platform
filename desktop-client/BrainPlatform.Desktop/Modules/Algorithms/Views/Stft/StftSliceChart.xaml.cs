using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;

public partial class StftSliceChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = CreateAxis(AxisAlignment.Bottom);
    private readonly NumericAxis yAxis = CreateAxis(AxisAlignment.Left);
    private readonly XyDataSeries<double, double> points = new() { SeriesName = "功率谱密度" };

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(StftPreview), typeof(StftSliceChart), new PropertyMetadata(null, OnChartChanged));
    public static readonly DependencyProperty IsSpectrumProperty = DependencyProperty.Register(
        nameof(IsSpectrum), typeof(bool), typeof(StftSliceChart), new PropertyMetadata(true, OnChartChanged));
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(double), typeof(StftSliceChart), new PropertyMetadata(0d, OnChartChanged));

    public StftPreview? Preview { get => (StftPreview?)GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public bool IsSpectrum { get => (bool)GetValue(IsSpectrumProperty); set => SetValue(IsSpectrumProperty, value); }
    public double SelectedIndex { get => (double)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    public StftSliceChart()
    {
        InitializeComponent();
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        yAxis.AxisTitle = "功率谱密度（dB re 1 μV²/Hz）";
        yAxis.AutoRange = AutoRange.Always;
        yAxis.GrowBy = new DoubleRange(0.08, 0.08);
        xAxis.AutoRange = AutoRange.Never;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            DataSeries = points,
            Stroke = Color.FromRgb(33, 111, 145),
            StrokeThickness = 2,
            DrawNaNAs = LineDrawMode.Gaps,
        });
        surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true,
            DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
        Content = surface;
    }

    private static NumericAxis CreateAxis(AxisAlignment alignment) => new()
    {
        AxisAlignment = alignment,
        FontSize = 12,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        DrawMajorBands = false,
        DrawMajorGridLines = true,
        DrawMinorGridLines = false,
    };

    private static void OnChartChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StftSliceChart)sender).UpdateSeries();

    private void UpdateSeries()
    {
        points.Clear();
        if (Preview is not { } preview) return;

        if (IsSpectrum)
        {
            var time = Math.Clamp((int)Math.Round(SelectedIndex), 0, preview.TimesSeconds.Length - 1);
            xAxis.AxisTitle = "频率（Hz）";
            points.SeriesName = $"{preview.TimesSeconds[time]:0.###} s 的功率谱密度";
            points.Append(preview.FrequenciesHz, preview.PowerDb[time].Select(value => value ?? double.NaN));
            SetXRange(preview.FrequenciesHz);
        }
        else
        {
            var frequency = Math.Clamp((int)Math.Round(SelectedIndex), 0, preview.FrequenciesHz.Length - 1);
            xAxis.AxisTitle = "时频中心时间（s）";
            points.SeriesName = $"{preview.FrequenciesHz[frequency]:0.###} Hz 的功率谱密度";
            points.Append(preview.TimesSeconds, preview.PowerDb.Select(row => row[frequency] ?? double.NaN));
            SetXRange(preview.TimesSeconds);
        }
    }

    private void SetXRange(double[] values)
    {
        var halfStep = values.Length > 1 ? (values[1] - values[0]) / 2 : 0.5;
        xAxis.VisibleRange = new DoubleRange(values[0] - halfStep, values[^1] + halfStep);
    }
}

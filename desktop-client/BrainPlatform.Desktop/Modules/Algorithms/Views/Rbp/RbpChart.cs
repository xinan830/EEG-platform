using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Rbp;

public sealed class RbpChart : UserControl
{
    private readonly SciChartSurface surface = new();
    private readonly NumericAxis xAxis = new()
    {
        FontSize = 13, DrawMajorBands = false, DrawMajorGridLines = false,
        DrawMinorGridLines = false, AutoRange = AutoRange.Never,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left, AxisTitle = "相对功率（%）",
        TextFormatting = "0%", FontSize = 13, DrawMajorBands = false,
        DrawMajorGridLines = true, DrawMinorGridLines = false,
        AutoRange = AutoRange.Never, VisibleRange = new DoubleRange(0, 1.05),
    };

    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(IReadOnlyList<RbpBandPoint>), typeof(RbpChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty WindowsProperty = DependencyProperty.Register(
        nameof(Windows), typeof(IReadOnlyList<RbpWindowPoint>), typeof(RbpChart), new PropertyMetadata(null, OnDataChanged));
    public static readonly DependencyProperty IsTrendProperty = DependencyProperty.Register(
        nameof(IsTrend), typeof(bool), typeof(RbpChart), new PropertyMetadata(false, OnDataChanged));

    public IReadOnlyList<RbpBandPoint>? Bands { get => (IReadOnlyList<RbpBandPoint>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public IReadOnlyList<RbpWindowPoint>? Windows { get => (IReadOnlyList<RbpWindowPoint>?)GetValue(WindowsProperty); set => SetValue(WindowsProperty, value); }
    public bool IsTrend { get => (bool)GetValue(IsTrendProperty); set => SetValue(IsTrendProperty, value); }

    public RbpChart()
    {
        surface.Background = Brushes.White;
        surface.ShowLicensingWarnings = false;
        surface.XAxes.Add(xAxis);
        surface.YAxes.Add(yAxis);
        surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true, DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
        Content = surface;
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RbpChart)sender).Rebuild();

    private void Rebuild()
    {
        surface.RenderableSeries.Clear();
        if (IsTrend)
        {
            BuildTrend();
            return;
        }
        BuildBars();
    }

    private void BuildBars()
    {
        var bands = Bands ?? [];
        xAxis.AxisTitle = "频段";
        xAxis.LabelProvider = new BandLabelProvider(bands.Select(band => band.Label).ToArray());
        xAxis.AutoTicks = false;
        xAxis.MajorDelta = 1;
        xAxis.MinorDelta = 1;
        xAxis.VisibleRange = new DoubleRange(-0.6, Math.Max(0.6, bands.Count - 0.4));
        foreach (var (band, index) in bands.Select((value, index) => (value, index)))
        {
            if (band.Share is not double share) continue;
            var points = new XyDataSeries<double, double> { SeriesName = $"{band.Label} · {band.ShareText}" };
            points.Append(index, share);
            surface.RenderableSeries.Add(new FastColumnRenderableSeries
            {
                DataSeries = points,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(band.Color)),
                StrokeThickness = 0,
                DataPointWidth = 0.6,
            });
        }
    }

    private void BuildTrend()
    {
        var windows = Windows ?? [];
        var bands = Bands ?? [];
        xAxis.AxisTitle = "窗口结束时间（s）";
        xAxis.LabelProvider = new NumericLabelProvider();
        xAxis.AutoTicks = true;
        xAxis.VisibleRange = windows.Count > 0
            ? new DoubleRange(Math.Max(0, windows[0].StartSeconds), Math.Max(windows[0].StartSeconds + 1, windows[^1].EndSeconds + 0.5))
            : new DoubleRange(0, 10);
        for (var index = 0; index < bands.Count; index++)
        {
            var points = new XyDataSeries<double, double> { SeriesName = bands[index].Label };
            foreach (var window in windows)
                points.Append(window.EndSeconds, window.Shares[index] ?? double.NaN);
            surface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                DataSeries = points,
                Stroke = (Color)ColorConverter.ConvertFromString(bands[index].Color),
                StrokeThickness = 2,
                DrawNaNAs = LineDrawMode.Gaps,
            });
        }
    }

    private sealed class BandLabelProvider(string[] labels) : NumericLabelProvider
    {
        public override string FormatLabel(IComparable dataValue)
        {
            var value = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
            var index = (int)Math.Round(value);
            return Math.Abs(value - index) < 1e-6 && index >= 0 && index < labels.Length ? labels[index] : "";
        }
    }
}

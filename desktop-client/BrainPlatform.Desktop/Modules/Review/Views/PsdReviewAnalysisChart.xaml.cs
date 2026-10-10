using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Review.Views;

/// <summary>
/// Independent SciChart host for a bounded PSD preview window. It renders
/// backend-provided arrays and never computes or interpolates scientific data.
/// </summary>
public partial class PsdReviewAnalysisChart : UserControl
{
    public static readonly DependencyProperty WindowProperty = DependencyProperty.Register(
        nameof(Window), typeof(WindowPreviewResponse), typeof(PsdReviewAnalysisChart),
        new PropertyMetadata(null, OnWindowChanged));

    private readonly NumericAxis xAxis = new()
    {
        AxisTitle = "频率 (Hz)",
        AxisAlignment = AxisAlignment.Bottom,
        DrawMajorBands = false,
        DrawMajorGridLines = true,
        DrawMinorGridLines = false,
        TextFormatting = "0.#",
        FontSize = 12,
    };

    private readonly NumericAxis yAxis = new()
    {
        AxisTitle = "功率谱密度 (μV²/Hz)",
        AxisAlignment = AxisAlignment.Left,
        DrawMajorBands = false,
        DrawMajorGridLines = true,
        DrawMinorGridLines = false,
        TextFormatting = "0.###",
        FontSize = 12,
    };

    public PsdReviewAnalysisChart()
    {
        InitializeComponent();
        Surface.Background = Brushes.White;
        Surface.ShowLicensingWarnings = false;
        Surface.XAxes.Add(xAxis);
        Surface.YAxes.Add(yAxis);
        Surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true,
            DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
    }

    public WindowPreviewResponse? Window
    {
        get => (WindowPreviewResponse?)GetValue(WindowProperty);
        set => SetValue(WindowProperty, value);
    }

    private static void OnWindowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PsdReviewAnalysisChart)sender).RenderWindow((WindowPreviewResponse?)args.NewValue);

    private void RenderWindow(WindowPreviewResponse? response)
    {
        Surface.RenderableSeries.Clear();
        if (response is null)
        {
            ShowEmpty("未绑定 PSD 结果");
            return;
        }

        if (!string.Equals(response.Status, "available", StringComparison.OrdinalIgnoreCase))
        {
            ShowEmpty(string.IsNullOrWhiteSpace(response.ReasonCodes.FirstOrDefault())
                ? $"当前窗口不可用：{response.Status}"
                : $"当前窗口不可用：{response.ReasonCodes[0]}");
            return;
        }

        ReviewPsdProjection projection;
        try
        {
            projection = ReviewPsdProjection.From(response);
        }
        catch (InvalidOperationException exception)
        {
            ShowEmpty(exception.Message);
            return;
        }
        var frequencies = projection.Frequencies;
        var values = projection.Values;

        var series = new XyDataSeries<double, double> { SeriesName = "PSD" };
        series.Append(frequencies, values);
        Surface.RenderableSeries.Add(new FastLineRenderableSeries
        {
            DataSeries = series,
            Stroke = Color.FromRgb(37, 99, 235),
            StrokeThickness = 2,
        });
        EmptyState.Visibility = Visibility.Collapsed;
        xAxis.VisibleRange = new DoubleRange(frequencies[0], frequencies[^1]);
        var min = values.Min();
        var max = values.Max();
        var pad = Math.Max(1e-6, Math.Max((max - min) * 0.08, Math.Abs(max) * 0.02));
        yAxis.VisibleRange = new DoubleRange(Math.Max(0, min - pad), max + pad);
    }

    private void ShowEmpty(string message)
    {
        StatusText.Text = message;
        EmptyState.Visibility = Visibility.Visible;
    }

}

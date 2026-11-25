using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Renders mutually-exclusive paired scientific comparison traces (light-gray baseline + dark-gray processed) and floating legend.</summary>
internal sealed class ReviewPreprocessingOverlayLayer
{
    private static readonly Color ScientificBaselineGray = Color.FromRgb(186, 198, 212);
    private static readonly Color ScientificProcessedDark = Color.FromRgb(15, 23, 42);
    private static readonly Color AlertAmberColor = Color.FromRgb(217, 119, 6);
    private static readonly Color BrandBlueColor = Color.FromRgb(37, 99, 235);
    private static readonly double[] InterpolatedDash = [5, 3];

    private readonly SciChartSurface surface;
    private readonly List<OverlaySeries> baselineSeries = [];
    private readonly List<OverlaySeries> processedSeries = [];
    private readonly Border legendBorder;
    private readonly StackPanel baselineSwatch;
    private readonly TextBlock legendText;

    public ReviewPreprocessingOverlayLayer(SciChartSurface surface)
    {
        this.surface = surface;
        baselineSwatch = CreateSwatch(ScientificBaselineGray, "浅灰 科学滤波基线");
        legendText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(baselineSwatch);
        content.Children.Add(CreateSwatch(ScientificProcessedDark, "深灰 科学处理后"));
        content.Children.Add(legendText);

        legendBorder = new Border
        {
            Child = content,
            Background = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 8, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
    }

    public UIElement LegendElement => legendBorder;

    public void EnsureSeries(int channelCount)
    {
        foreach (var item in baselineSeries)
            surface.RenderableSeries.Remove(item.LineSeries);
        foreach (var item in processedSeries)
            surface.RenderableSeries.Remove(item.LineSeries);
        baselineSeries.Clear();
        processedSeries.Clear();

        for (var i = 0; i < channelCount; i++)
        {
            var baseData = new XyDataSeries<double, double>();
            var baseLine = new FastLineRenderableSeries
            {
                DataSeries = baseData,
                Stroke = ScientificBaselineGray,
                StrokeThickness = 1,
                DrawNaNAs = LineDrawMode.Gaps,
                IsVisible = false,
            };
            surface.RenderableSeries.Add(baseLine);
            baselineSeries.Add(new OverlaySeries(baseData, baseLine));

            var procData = new XyDataSeries<double, double>();
            var procLine = new FastLineRenderableSeries
            {
                DataSeries = procData,
                Stroke = ScientificProcessedDark,
                StrokeThickness = 1,
                DrawNaNAs = LineDrawMode.Gaps,
                IsVisible = false,
            };
            surface.RenderableSeries.Add(procLine);
            processedSeries.Add(new OverlaySeries(procData, procLine));
        }
    }

    public void Clear()
    {
        foreach (var item in baselineSeries)
        {
            item.Data.Clear();
            item.LineSeries.IsVisible = false;
        }
        foreach (var item in processedSeries)
        {
            item.Data.Clear();
            item.LineSeries.IsVisible = false;
        }
        legendBorder.Visibility = Visibility.Collapsed;
    }

    public void ApplyVisuals(
        RecordingReviewViewModel? viewModel,
        IReadOnlyList<string> channelNames,
        IReadOnlyList<string>? configuredColors,
        IReadOnlyList<FastLineRenderableSeries> baseSeries,
        Grid labelsGrid,
        double plotHeight,
        ScreenScaleContext screenScale)
    {
        var prep = viewModel?.Preprocessing;
        var comparisonReady = prep?.CanDisplayScientificComparison(channelNames) == true &&
            baselineSeries.Count >= channelNames.Count && processedSeries.Count >= channelNames.Count;
        var overlayWindow = comparisonReady ? prep!.CurrentOverlayWindow! : null;

        UpdateChannelBadges(labelsGrid, channelNames, configuredColors, prep);

        var traceCount = Math.Min(channelNames.Count, baseSeries.Count);
        for (var i = 0; i < traceCount; i++)
        {
            var baseColor = WaveformTraceColors.ToColor(configuredColors?.ElementAtOrDefault(i), i);
            baseSeries[i].Stroke = baseColor;
            baseSeries[i].StrokeThickness = 1;
            baseSeries[i].StrokeDashArray = null;
            // Mutually exclusive: hide ordinary colored series when paired scientific comparison is ready.
            baseSeries[i].IsVisible = !comparisonReady;
        }

        if (!comparisonReady || overlayWindow is null || baselineSeries.Count < traceCount || processedSeries.Count < traceCount)
        {
            Clear();
            return;
        }

        var sensitivity = viewModel?.SensitivityMicrovoltsPerMillimeter ?? 10.0;
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            traceCount, plotHeight, sensitivity, screenScale.MillimetersPerDipY);

        for (var i = 0; i < traceCount; i++)
        {
            var projected = ReviewPreprocessingOverlayProjection.ProjectRow(
                overlayWindow, channelNames[i], i, traceCount, displayScale);
            ApplySeriesData(baselineSeries[i], projected.DrawBaseline, projected.BaselineX, projected.BaselineY, dash: null);
            ApplySeriesData(processedSeries[i], projected.DrawProcessed, projected.ProcessedX, projected.ProcessedY,
                dash: projected.IsInterpolated ? InterpolatedDash : null);
        }

        baselineSwatch.Visibility = overlayWindow.EnhancedPreprocessingActive ? Visibility.Visible : Visibility.Collapsed;
        legendText.Text = overlayWindow.EnhancedPreprocessingActive
            ? "· 零相位 1–50 Hz，陷波 50 Hz（Q=30）；顶部滤波仅用于彩色阅图"
            : "· 仅基础科学滤波（零相位 1–50 Hz，陷波 50 Hz Q=30，增强全关前后一致）；顶部滤波仅用于彩色阅图";
        legendBorder.Visibility = Visibility.Visible;
    }

    private static void ApplySeriesData(OverlaySeries series, bool visible, double[]? xs, double[]? ys, double[]? dash)
    {
        if (!visible || xs is null || ys is null || xs.Length == 0)
        {
            series.Data.Clear();
            series.LineSeries.IsVisible = false;
            return;
        }
        series.LineSeries.StrokeDashArray = dash;
        using (series.Data.SuspendUpdates())
        {
            series.Data.Clear();
            series.Data.Append(xs, ys);
        }
        series.LineSeries.IsVisible = true;
    }

    private static StackPanel CreateSwatch(Color color, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Border
        {
            Width = 14, Height = 2, CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private static void UpdateChannelBadges(
        Grid labelsGrid,
        IReadOnlyList<string> channelNames,
        IReadOnlyList<string>? configuredColors,
        ReviewPreprocessingViewModel? prep)
    {
        if (labelsGrid.Children.Count != channelNames.Count) return;
        for (var i = 0; i < channelNames.Count; i++)
        {
            var name = channelNames[i];
            var baseColor = WaveformTraceColors.ToColor(configuredColors?.ElementAtOrDefault(i), i);
            var badge = prep?.ResolveChannelBadge(name)
                        ?? new ChannelVisualBadge(ChannelVisualPreprocessingState.Normal, "");

            if (labelsGrid.Children[i] is not FrameworkElement element) continue;
            element.ToolTip = string.IsNullOrEmpty(badge.Tooltip) ? null : badge.Tooltip;
            if (element is StackPanel panel && panel.Children.Count >= 2 &&
                panel.Children[0] is Ellipse dot && panel.Children[1] is TextBlock text)
            {
                switch (badge.State)
                {
                    case ChannelVisualPreprocessingState.CandidateBad:
                    case ChannelVisualPreprocessingState.Excluded:
                        dot.Fill = new SolidColorBrush(AlertAmberColor);
                        dot.Visibility = Visibility.Visible;
                        text.Foreground = new SolidColorBrush(badge.State == ChannelVisualPreprocessingState.Excluded
                            ? Color.FromRgb(148, 163, 184)
                            : Color.FromRgb(180, 83, 9));
                        break;
                    case ChannelVisualPreprocessingState.Interpolated:
                        dot.Fill = new SolidColorBrush(BrandBlueColor);
                        dot.Visibility = Visibility.Visible;
                        text.Foreground = new SolidColorBrush(BrandBlueColor);
                        break;
                    default:
                        dot.Visibility = Visibility.Collapsed;
                        text.Foreground = new SolidColorBrush(baseColor);
                        break;
                }
            }
        }
    }

    private sealed record OverlaySeries(XyDataSeries<double, double> Data, FastLineRenderableSeries LineSeries);
}

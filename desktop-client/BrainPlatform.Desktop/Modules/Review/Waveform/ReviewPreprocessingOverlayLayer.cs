using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Renders cleaned-signal overlay traces (with cyan dashed style for interpolated bad channels) and bad-channel badges.</summary>
internal sealed class ReviewPreprocessingOverlayLayer
{
    private static readonly Color DimmedOriginalColor = Color.FromRgb(203, 213, 225);
    private static readonly Color InterpolatedCyanColor = Color.FromRgb(8, 145, 178);
    private static readonly Color CleanedDarkColor = Color.FromRgb(15, 23, 42);
    private static readonly double[] InterpolatedDash = [5, 3];

    private readonly SciChartSurface surface;
    private readonly List<OverlaySeries> overlaySeries = [];

    public ReviewPreprocessingOverlayLayer(SciChartSurface surface)
    {
        this.surface = surface;
    }

    public void EnsureSeries(int channelCount)
    {
        foreach (var item in overlaySeries)
            surface.RenderableSeries.Remove(item.LineSeries);
        overlaySeries.Clear();

        for (var i = 0; i < channelCount; i++)
        {
            var data = new XyDataSeries<double, double>();
            var line = new FastLineRenderableSeries
            {
                DataSeries = data,
                Stroke = CleanedDarkColor,
                StrokeThickness = 1.35,
                DrawNaNAs = LineDrawMode.Gaps,
                IsVisible = false,
            };
            surface.RenderableSeries.Add(line);
            overlaySeries.Add(new OverlaySeries(data, line));
        }
    }

    public void Clear()
    {
        foreach (var item in overlaySeries)
        {
            item.Data.Clear();
            item.LineSeries.IsVisible = false;
        }
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
        var overlayActive = prep is { IsOverlayCompareEnabled: true, CurrentOverlayWindow: not null };
        var overlayWindow = overlayActive ? prep!.CurrentOverlayWindow! : null;
        var interpolatedSet = overlayWindow is not null
            ? overlayWindow.InterpolatedChannels.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Update left channel rail badges & tooltips + base trace appearance.
        UpdateChannelBadges(labelsGrid, channelNames, configuredColors, prep);

        var traceCount = Math.Min(channelNames.Count, baseSeries.Count);
        for (var i = 0; i < traceCount; i++)
        {
            var baseColor = WaveformTraceColors.ToColor(configuredColors?.ElementAtOrDefault(i), i);
            var badge = prep?.ResolveChannelBadge(channelNames[i])
                        ?? new ChannelVisualBadge(ChannelVisualPreprocessingState.Normal, "");

            if (overlayActive)
            {
                baseSeries[i].Stroke = DimmedOriginalColor;
                baseSeries[i].StrokeThickness = 1.0;
                baseSeries[i].StrokeDashArray = null;
            }
            else if (badge.State == ChannelVisualPreprocessingState.Interpolated)
            {
                baseSeries[i].Stroke = InterpolatedCyanColor;
                baseSeries[i].StrokeThickness = 1.2;
                baseSeries[i].StrokeDashArray = InterpolatedDash;
            }
            else if (badge.State == ChannelVisualPreprocessingState.Excluded)
            {
                baseSeries[i].Stroke = DimmedOriginalColor;
                baseSeries[i].StrokeThickness = 0.9;
                baseSeries[i].StrokeDashArray = InterpolatedDash;
            }
            else
            {
                baseSeries[i].Stroke = baseColor;
                baseSeries[i].StrokeThickness = 1.0;
                baseSeries[i].StrokeDashArray = null;
            }
        }

        // 2. Populate cleaned overlay traces when overlay compare is enabled.
        if (!overlayActive || overlayWindow is null || overlaySeries.Count < traceCount)
        {
            Clear();
            return;
        }

        var sensitivity = viewModel?.SensitivityMicrovoltsPerMillimeter ?? 10.0;
        var displayScale = ScreenScaleCalculator.VerticalDisplayScale(
            traceCount, plotHeight, sensitivity, screenScale.MillimetersPerDipY);
        var times = overlayWindow.TimesSeconds;

        for (var i = 0; i < traceCount; i++)
        {
            var ch = channelNames[i];
            var ov = overlaySeries[i];
            if (!TryGetTrace(overlayWindow.TracesUv, ch, out var valuesUv) || valuesUv is null || valuesUv.Count == 0)
            {
                ov.Data.Clear();
                ov.LineSeries.IsVisible = false;
                continue;
            }
            var baseline = traceCount - i - 0.5;
            var count = Math.Min(times.Count, valuesUv.Count);
            var xs = new double[count];
            var ys = new double[count];
            for (var k = 0; k < count; k++)
            {
                xs[k] = times[k];
                ys[k] = valuesUv[k] is { } uv ? baseline + uv * displayScale : double.NaN;
            }
            var isInterpolated = interpolatedSet.Contains(ch) ||
                                 (prep?.ResolveChannelBadge(ch).State == ChannelVisualPreprocessingState.Interpolated);
            ov.LineSeries.Stroke = isInterpolated ? InterpolatedCyanColor : CleanedDarkColor;
            ov.LineSeries.StrokeThickness = isInterpolated ? 1.45 : 1.3;
            ov.LineSeries.StrokeDashArray = isInterpolated ? InterpolatedDash : null;
            using (ov.Data.SuspendUpdates())
            {
                ov.Data.Clear();
                ov.Data.Append(xs, ys);
            }
            ov.LineSeries.IsVisible = true;
        }
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
                        dot.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        dot.Visibility = Visibility.Visible;
                        text.Foreground = new SolidColorBrush(badge.State == ChannelVisualPreprocessingState.Excluded
                            ? Color.FromRgb(148, 163, 184)
                            : Color.FromRgb(185, 28, 28));
                        break;
                    case ChannelVisualPreprocessingState.Interpolated:
                        dot.Fill = new SolidColorBrush(InterpolatedCyanColor);
                        dot.Visibility = Visibility.Visible;
                        text.Foreground = new SolidColorBrush(InterpolatedCyanColor);
                        break;
                    default:
                        dot.Visibility = Visibility.Collapsed;
                        text.Foreground = new SolidColorBrush(baseColor);
                        break;
                }
            }
        }
    }

    private static bool TryGetTrace(
        IReadOnlyDictionary<string, IReadOnlyList<double?>> traces,
        string channelName,
        out IReadOnlyList<double?>? trace)
    {
        if (traces.TryGetValue(channelName, out trace)) return true;
        foreach (var pair in traces)
        {
            if (string.Equals(pair.Key, channelName, StringComparison.OrdinalIgnoreCase))
            {
                trace = pair.Value;
                return true;
            }
        }
        trace = null;
        return false;
    }

    private sealed record OverlaySeries(XyDataSeries<double, double> Data, FastLineRenderableSeries LineSeries);
}

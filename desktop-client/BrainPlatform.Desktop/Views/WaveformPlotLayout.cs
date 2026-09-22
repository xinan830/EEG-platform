using System.Windows;
using System.Windows.Controls;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Shared plot-area geometry for the waveform chart and its external labels.
/// It contains no waveform or playback state.
/// </summary>
internal static class WaveformPlotLayout
{
    public static Rect GetPlotArea(SciChartSurface surface)
    {
        if (surface.GridLinesPanel is not FrameworkElement gridLines ||
            gridLines.ActualWidth <= 0 ||
            gridLines.ActualHeight <= 0)
        {
            return new Rect(0, 0, Math.Max(1, surface.ActualWidth), Math.Max(1, surface.ActualHeight));
        }

        var origin = gridLines.TranslatePoint(new Point(), surface);
        return new Rect(origin.X, origin.Y, gridLines.ActualWidth, gridLines.ActualHeight);
    }

    public static void SyncLabelPlotArea(
        SciChartSurface surface,
        FrameworkElement labels,
        ref Thickness? lastLabelPlotMargin)
    {
        var plotArea = GetPlotArea(surface);
        if (surface.ActualHeight <= 0 || plotArea.Height <= 0)
        {
            return;
        }

        var margin = new Thickness(
            0,
            Math.Max(0, plotArea.Top),
            0,
            Math.Max(0, surface.ActualHeight - plotArea.Bottom));
        if (lastLabelPlotMargin is { } previous &&
            Math.Abs(previous.Top - margin.Top) < 0.1 &&
            Math.Abs(previous.Bottom - margin.Bottom) < 0.1)
        {
            return;
        }

        lastLabelPlotMargin = margin;
        labels.Margin = margin;
    }
}

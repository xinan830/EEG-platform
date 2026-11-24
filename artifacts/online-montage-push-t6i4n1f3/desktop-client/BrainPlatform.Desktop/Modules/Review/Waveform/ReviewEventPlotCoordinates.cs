using System.Windows;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Projects plot pixels into the displayed recording time, never playback-relative time.</summary>
internal static class ReviewEventPlotCoordinates
{
    public static bool TryGetSeconds(Rect plot, Point point, double start, double end, out double seconds)
    {
        seconds = double.NaN;
        if (plot.IsEmpty || plot.Width <= 0 || plot.Height <= 0 || !plot.Contains(point) ||
            !double.IsFinite(start) || !double.IsFinite(end) || end <= start) return false;
        seconds = start + (point.X - plot.Left) / plot.Width * (end - start);
        return double.IsFinite(seconds);
    }
}

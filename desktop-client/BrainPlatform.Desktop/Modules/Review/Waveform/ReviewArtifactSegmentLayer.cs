using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SciChart.Charting.Visuals;
using SciChart.Data.Model;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Renders translucent vertical bad-segment bands (amplitude PTP / EMG muscle) over the Review SciChart surface.</summary>
internal sealed class ReviewArtifactSegmentLayer
{
    private readonly List<BandVisual> visuals = [];
    private readonly SciChartSurface surface;
    private readonly FrameworkElement host;

    public ReviewArtifactSegmentLayer(SciChartSurface surface, FrameworkElement host)
    {
        this.surface = surface;
        this.host = host;
        Canvas.IsHitTestVisible = true;
    }

    public Canvas Canvas { get; } = new();

    public void Clear()
    {
        Canvas.Children.Clear();
        visuals.Clear();
    }

    public void Rebuild(RecordingReviewViewModel? viewModel)
    {
        Clear();
        var markers = viewModel?.Preprocessing.ArtifactMarkers;
        if (markers is null || markers.Count == 0) return;

        foreach (var marker in markers)
        {
            var isMuscle = string.Equals(marker.Reason, "muscle_zscore", StringComparison.OrdinalIgnoreCase);
            var isManual = !isMuscle && !string.Equals(marker.Reason, "amplitude_ptp", StringComparison.OrdinalIgnoreCase);
            var color = isMuscle
                ? Color.FromRgb(217, 119, 6)    // Amber for EMG muscle
                : isManual
                    ? Color.FromRgb(124, 58, 237) // Purple for manual
                    : Color.FromRgb(220, 38, 38); // Red for amplitude PTP

            var band = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1, 0, 1, 0),
                ToolTip = marker.Tooltip,
                IsHitTestVisible = true,
            };

            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1.5, 5, 1.5),
                ToolTip = marker.Tooltip,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = marker.Label,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                },
            };

            Canvas.Children.Add(band);
            Canvas.Children.Add(pill);
            visuals.Add(new BandVisual(band, pill, marker.StartSeconds, marker.EndSeconds));
        }

        UpdatePositions();
    }

    public void UpdatePositions()
    {
        if (visuals.Count == 0) return;
        if (surface.XAxes.FirstOrDefault()?.VisibleRange is not DoubleRange range)
        {
            foreach (var v in visuals) { v.Band.Visibility = Visibility.Collapsed; v.Pill.Visibility = Visibility.Collapsed; }
            return;
        }
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        if (plotArea.Width <= 0 || plotArea.Height <= 0) return;

        var origin = surface.TranslatePoint(plotArea.TopLeft, host);
        var minS = range.Min;
        var maxS = range.Max;
        var span = Math.Max(0.001, maxS - minS);

        foreach (var v in visuals)
        {
            if (v.EndSeconds <= minS || v.StartSeconds >= maxS)
            {
                v.Band.Visibility = Visibility.Collapsed;
                v.Pill.Visibility = Visibility.Collapsed;
                continue;
            }
            var leftFrac = Math.Clamp((v.StartSeconds - minS) / span, 0, 1);
            var rightFrac = Math.Clamp((v.EndSeconds - minS) / span, 0, 1);
            var xLeft = origin.X + leftFrac * plotArea.Width;
            var width = Math.Max(3.0, (rightFrac - leftFrac) * plotArea.Width);

            Canvas.SetLeft(v.Band, xLeft);
            Canvas.SetTop(v.Band, origin.Y);
            v.Band.Width = width;
            v.Band.Height = plotArea.Height;
            v.Band.Visibility = Visibility.Visible;

            Canvas.SetLeft(v.Pill, Math.Min(origin.X + plotArea.Width - 80, xLeft + 2));
            Canvas.SetTop(v.Pill, origin.Y + 4);
            v.Pill.Visibility = Visibility.Visible;
        }
    }

    private sealed record BandVisual(Border Band, Border Pill, double StartSeconds, double EndSeconds);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Waveform;

internal static class WaveformEventTimeLabel
{
    public static void Add(Canvas overlay, double markerX, double top, string text, Color color, string tooltip)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = tooltip,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var left = markerX + 5;
        if (left + label.DesiredSize.Width > overlay.ActualWidth)
        {
            left = markerX - label.DesiredSize.Width - 5;
        }

        Canvas.SetLeft(label, Math.Clamp(left, 0, Math.Max(0, overlay.ActualWidth - label.DesiredSize.Width)));
        Canvas.SetTop(label, Math.Clamp(top, 0, Math.Max(0, overlay.ActualHeight - label.DesiredSize.Height)));
        overlay.Children.Add(label);
    }
}

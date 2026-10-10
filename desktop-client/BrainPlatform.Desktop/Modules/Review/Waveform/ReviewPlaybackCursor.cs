using System.Windows;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Draws a display-only playhead inside the waveform plot.</summary>
internal sealed class ReviewPlaybackCursor : FrameworkElement
{
    private static readonly Brush CursorBrush = CreateBrush();
    private static readonly Pen CursorPen = CreatePen();
    private Rect plotArea;
    private double fraction;

    public ReviewPlaybackCursor()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    internal void SetPosition(Rect area, double positionFraction)
    {
        positionFraction = Math.Clamp(positionFraction, 0, 1);
        if (plotArea == area && fraction == positionFraction) return;
        plotArea = area;
        fraction = positionFraction;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (plotArea.IsEmpty || plotArea.Width < 1 || plotArea.Height < 1) return;

        var x = plotArea.Left + Math.Clamp(fraction * plotArea.Width, 0.5, plotArea.Width - 0.5);
        var markerHeight = Math.Min(7, plotArea.Height);
        var marker = new StreamGeometry();
        using (var geometry = marker.Open())
        {
            geometry.BeginFigure(new Point(Math.Max(plotArea.Left, x - 4), plotArea.Top), true, true);
            geometry.LineTo(new Point(Math.Min(plotArea.Right, x + 4), plotArea.Top), true, false);
            geometry.LineTo(new Point(x, plotArea.Top + markerHeight), true, false);
        }
        marker.Freeze();
        drawingContext.PushClip(new RectangleGeometry(plotArea));
        drawingContext.DrawLine(CursorPen, new Point(x, plotArea.Top + markerHeight), new Point(x, plotArea.Bottom));
        drawingContext.DrawGeometry(CursorBrush, null, marker);
        drawingContext.Pop();
    }

    private static Brush CreateBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(75, 85, 99));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen()
    {
        var pen = new Pen(CursorBrush, 1)
        {
            DashStyle = new DashStyle([3d, 3d], 0),
            DashCap = PenLineCap.Flat,
        };
        pen.Freeze();
        return pen;
    }
}

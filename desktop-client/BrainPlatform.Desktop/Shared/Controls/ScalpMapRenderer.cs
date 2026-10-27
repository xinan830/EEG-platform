using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls;

/// <summary>Shared scalp-map canvas geometry and static electrode rendering.</summary>
public static class ScalpMapRenderer
{
    private const string OutlineColor = "#64748B";
    private const string HeadFillColor = "#F8FAFC";
    private const double RadiusScale = 0.39;

    public static Rect DrawHead(DrawingContext drawingContext, double width, double height, double verticalOffset, double reservedHeight, double pixelsPerDip)
    {
        if (width < 80 || height < 80) return Rect.Empty;
        var center = new Point(width / 2, height / 2 + verticalOffset);
        var radius = Math.Min(width, height - reservedHeight) * RadiusScale;
        if (radius <= 0) return Rect.Empty;
        var outline = new Pen(Brush(OutlineColor), 1.5);
        drawingContext.DrawEllipse(Brush(HeadFillColor), outline, center, radius, radius);
        DrawOrientation(drawingContext, center, radius, outline, pixelsPerDip);
        return new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    public static Point ToCanvas(double x, double y, Rect headBounds) =>
        new(headBounds.X + headBounds.Width / 2 + x * headBounds.Width / 2,
            headBounds.Y + headBounds.Height / 2 + y * headBounds.Height / 2);

    public static void DrawElectrode(DrawingContext context, string label, Point position, bool isHardwareLandmark, bool isManual, double pixelsPerDip)
    {
        var color = isHardwareLandmark
            ? label.Equals("REF", StringComparison.OrdinalIgnoreCase) ? "#7C3AED" : "#059669"
            : isManual ? "#D97706" : "#2563EB";
        var markerSize = isHardwareLandmark ? 5 : 3.5;
        context.DrawEllipse(Brush(color), null, position, markerSize, markerSize);
        context.DrawText(new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, Brush("#0F172A"), pixelsPerDip),
            new Point(position.X + 4, position.Y - 7));
    }

    private static void DrawOrientation(DrawingContext context, Point center, double radius, Pen outline, double pixelsPerDip)
    {
        var nose = new StreamGeometry();
        using (var geometry = nose.Open())
        {
            geometry.BeginFigure(new Point(center.X - 9, center.Y - radius + 2), false, false);
            geometry.LineTo(new Point(center.X, center.Y - radius - 12), true, false);
            geometry.LineTo(new Point(center.X + 9, center.Y - radius + 2), true, false);
        }
        context.DrawGeometry(null, outline, nose);
        context.DrawText(new FormattedText("前", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, Brush(OutlineColor), pixelsPerDip), new Point(center.X - 5, center.Y - radius - 25));
        DrawEar(context, center, radius, -1, outline);
        DrawEar(context, center, radius, 1, outline);
    }

    private static void DrawEar(DrawingContext context, Point center, double radius, int side, Pen outline)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        var x = center.X + side * radius;
        path.BeginFigure(new Point(x, center.Y - 12), false, false);
        path.LineTo(new Point(x + side * 8, center.Y - 8), true, false);
        path.LineTo(new Point(x + side * 8, center.Y + 8), true, false);
        path.LineTo(new Point(x, center.Y + 12), true, false);
        context.DrawGeometry(null, outline, geometry);
    }

    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}

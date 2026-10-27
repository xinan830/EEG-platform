using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

/// <summary>Draws directional display-reference relationships without plotting EEG values.</summary>
public sealed class MontageDirectionMap : FrameworkElement
{
    public static readonly DependencyProperty ArrowsProperty = DependencyProperty.Register(
        nameof(Arrows), typeof(IEnumerable<MontageDirectionArrow>), typeof(MontageDirectionMap),
        new FrameworkPropertyMetadata(null, (dependencyObject, _) => ((MontageDirectionMap)dependencyObject).InvalidateVisual()));

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable<MontageDirectionPoint>), typeof(MontageDirectionMap),
        new FrameworkPropertyMetadata(null, (dependencyObject, _) => ((MontageDirectionMap)dependencyObject).InvalidateVisual()));

    public IEnumerable<MontageDirectionArrow>? Arrows
    {
        get => (IEnumerable<MontageDirectionArrow>?)GetValue(ArrowsProperty);
        set => SetValue(ArrowsProperty, value);
    }

    public IEnumerable<MontageDirectionPoint>? Points
    {
        get => (IEnumerable<MontageDirectionPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var headBounds = ScalpMapRenderer.DrawHead(drawingContext, ActualWidth, ActualHeight, 5, 24, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (headBounds.IsEmpty) return;
        foreach (var arrow in Arrows ?? [])
        {
            var from = ScalpMapRenderer.ToCanvas(arrow.FromX, arrow.FromY, headBounds);
            var to = ScalpMapRenderer.ToCanvas(arrow.ToX, arrow.ToY, headBounds);
            DrawArrow(drawingContext, from, to, Brush("#2563EB"));
        }
        foreach (var electrode in Points ?? [])
        {
            var label = electrode.Label;
            var point = ScalpMapRenderer.ToCanvas(electrode.X, electrode.Y, headBounds);
            ScalpMapRenderer.DrawElectrode(drawingContext, label, point,
                label.Equals("REF", StringComparison.OrdinalIgnoreCase) || label.Equals("GND", StringComparison.OrdinalIgnoreCase),
                false, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }

    private static void DrawArrow(DrawingContext context, Point from, Point to, Brush brush)
    {
        var vector = to - from;
        if (vector.Length < 2) return;
        vector.Normalize();
        var end = to - vector * 8;
        context.DrawLine(new Pen(brush, 1.5), from, end);
        var perpendicular = new Vector(-vector.Y, vector.X);
        var head = new StreamGeometry();
        using (var geometry = head.Open())
        {
            geometry.BeginFigure(end, true, true);
            geometry.LineTo(end - vector * 7 + perpendicular * 3.5, true, false);
            geometry.LineTo(end - vector * 7 - perpendicular * 3.5, true, false);
        }
        context.DrawGeometry(brush, null, head);
    }

    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}

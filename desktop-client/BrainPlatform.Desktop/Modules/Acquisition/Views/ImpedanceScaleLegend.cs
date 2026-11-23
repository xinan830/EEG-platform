using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Acquisition.Views;

/// <summary>Draws the same three kΩ anchors used for electrode colors.</summary>
public sealed class ImpedanceScaleLegend : FrameworkElement
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale),
        typeof(ImpedanceColorScale), typeof(ImpedanceScaleLegend),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public ImpedanceColorScale? Scale
    {
        get => (ImpedanceColorScale?)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth < 100) return;
        var bar = new Rect(30, 28, Math.Max(1, ActualWidth - 60), 10);
        context.DrawRoundedRectangle(Scale?.Brush ?? (Brush)Brushes.LightSlateGray, null, bar, 5, 5);
        if (Scale is not { } scale) return;
        foreach (var (value, offset) in new[] { (scale.Minimum, 0d), (scale.Middle, scale.MiddleOffset), (scale.Maximum, 1d) })
        {
            var x = bar.X + offset * bar.Width;
            context.DrawLine(new Pen(Brushes.SlateGray, 1), new Point(x, 26), new Point(x, 42));
            var text = new FormattedText($"{value:g} kΩ", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var labelY = offset > 0 && offset < 1 ? 44 : 5;
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width)), labelY));
        }
    }
}

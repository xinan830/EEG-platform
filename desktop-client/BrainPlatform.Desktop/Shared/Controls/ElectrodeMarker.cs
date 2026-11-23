using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls;

/// <summary>Shared named electrode circle; optional readings sit above the circle.</summary>
public sealed class ElectrodeMarker : Button
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label),
        typeof(string), typeof(ElectrodeMarker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(nameof(Reading),
        typeof(string), typeof(ElectrodeMarker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected),
        typeof(bool), typeof(ElectrodeMarker), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public ElectrodeMarker()
    {
        Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(Grid)) };
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Reading { get => (string)GetValue(ReadingProperty); set => SetValue(ReadingProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    public static double Diameter(Rect headBounds) => Math.Clamp(headBounds.Width * 0.075, 22, 40);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var diameter = Math.Max(1, Math.Min(ActualWidth - 16, ActualHeight - 20));
        Draw(context, Label, new Point(ActualWidth / 2, ActualHeight - diameter / 2), diameter,
            Background, BorderBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip, Reading, IsSelected || IsKeyboardFocused);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == BackgroundProperty || args.Property == BorderBrushProperty ||
            args.Property == IsKeyboardFocusedProperty) InvalidateVisual();
    }

    public static void Draw(DrawingContext context, string label, Point center, double diameter,
        Brush fill, Brush outline, double pixelsPerDip, string? reading = null, bool selected = false)
    {
        var radius = diameter / 2;
        if (selected) context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2),
            center, radius + 3, radius + 3);
        context.DrawEllipse(fill, new Pen(outline, 1.4), center, radius, radius);
        var foreground = fill is SolidColorBrush solid &&
            0.2126 * solid.Color.R + 0.7152 * solid.Color.G + 0.0722 * solid.Color.B < 135
            ? Brushes.White : Brushes.Black;
        var fontSize = Math.Clamp(diameter * 0.36, 8, 14);
        var text = Text(label, fontSize, foreground, pixelsPerDip);
        if (text.Width > diameter - 4) text.SetFontSize(fontSize * (diameter - 4) / text.Width);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        if (!string.IsNullOrWhiteSpace(reading))
        {
            var value = Text(reading, Math.Clamp(diameter * 0.31, 9, 12), Brushes.DimGray, pixelsPerDip);
            context.DrawText(value, new Point(center.X - value.Width / 2, center.Y - radius - value.Height - 3));
        }
    }

    private static FormattedText Text(string value, double size, Brush brush, double pixelsPerDip) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
            size, brush, pixelsPerDip);
}

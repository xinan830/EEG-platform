using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls;

/// <summary>Shared icon-and-label action, using the client's existing Windows icon font.</summary>
public sealed class EegToolButton : Button
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EegToolButton), new PropertyMetadata(string.Empty));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource), typeof(ImageSource), typeof(EegToolButton), new PropertyMetadata(null));

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    static EegToolButton() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(EegToolButton), new FrameworkPropertyMetadata(typeof(EegToolButton)));
}

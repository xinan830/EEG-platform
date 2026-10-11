using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Behaviors;

/// <summary>Page-owned caption content; contains no session or scientific state.</summary>
public static class EegSessionPresentation
{
    public static readonly DependencyProperty MenuProperty = DependencyProperty.RegisterAttached(
        "Menu", typeof(Menu), typeof(EegSessionPresentation));

    public static Menu? GetMenu(DependencyObject element) => (Menu?)element.GetValue(MenuProperty);
    public static void SetMenu(DependencyObject element, Menu? value) => element.SetValue(MenuProperty, value);
}

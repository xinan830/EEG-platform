using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Views;

/// <summary>Applies the shared slim style to scroll bars generated inside a page.</summary>
public static class SlimScrollBarBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SlimScrollBarBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        element.Loaded += (_, _) => element.Dispatcher.BeginInvoke(
            () => ApplyToVerticalScrollBars(element),
            DispatcherPriority.Loaded);
    }

    private static void ApplyToVerticalScrollBars(DependencyObject root)
    {
        var style = Application.Current?.TryFindResource("SlimTableScrollBarStyle") as Style;
        if (style is null)
        {
            return;
        }

        foreach (var scrollBar in FindVisualDescendants<ScrollBar>(root))
        {
            if (scrollBar.Orientation == Orientation.Vertical)
            {
                scrollBar.Style = style;
            }
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}

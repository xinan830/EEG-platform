using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Helper arranging the responsive auto-grid / narrow scroll / quick-bar in default mode.</summary>
internal static class AutoAnalysisGridLayout
{
    public static void ArrangeRightSurface(
        Grid rightSurface,
        Grid rightGrid,
        ScrollViewer narrowScroller,
        StackPanel narrowStack,
        IReadOnlyList<object> allSlots,
        IReadOnlyList<object> visibleSlots,
        bool isWide,
        double narrowMinHeight,
        Func<object, DockCardChrome> resolveCard,
        Func<object, bool> isFocused)
    {
        rightSurface.Children.Clear();
        rightGrid.Children.Clear();
        rightGrid.RowDefinitions.Clear();
        rightGrid.ColumnDefinitions.Clear();
        narrowStack.Children.Clear();

        var showMultiChrome = allSlots.Count > 1;
        if (visibleSlots.Count >= 3 && !isWide)
        {
            foreach (var slot in visibleSlots)
            {
                var card = resolveCard(slot);
                Detach(card);
                card.SetChrome(true, showMultiChrome, isFocused(slot));
                card.MinHeight = narrowMinHeight;
                card.Height = Math.Max(narrowMinHeight, 340);
                narrowStack.Children.Add(card);
            }
            rightSurface.Children.Add(narrowScroller);
            return;
        }

        ArrangeGrid(rightGrid, visibleSlots, showMultiChrome, resolveCard, isFocused);
        rightSurface.Children.Add(rightGrid);
    }

    public static void RebuildQuickBar(
        Border quickBar,
        StackPanel quickBarPills,
        IReadOnlyList<object> slots,
        Func<object, bool> isFocused,
        Func<object, string?> readTitle,
        Action<object> onFocusSlot)
    {
        if (slots.Count <= 1)
        {
            quickBar.Visibility = Visibility.Collapsed;
            quickBarPills.Children.Clear();
            return;
        }
        quickBar.Visibility = Visibility.Visible;
        quickBarPills.Children.Clear();
        quickBarPills.Children.Add(new TextBlock
        {
            Text = $"并行分析 ({slots.Count})：",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
        });
        foreach (var slot in slots)
        {
            var focused = isFocused(slot);
            var button = new Button
            {
                Content = readTitle(slot) ?? "算法",
                FontSize = 11.5,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(focused ? Color.FromRgb(219, 234, 254) : Color.FromRgb(248, 250, 252)),
                Foreground = new SolidColorBrush(focused ? Color.FromRgb(37, 99, 235) : Color.FromRgb(51, 65, 85)),
                BorderBrush = new SolidColorBrush(focused ? Color.FromRgb(147, 197, 253) : Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Tag = slot,
            };
            button.Click += (_, _) => onFocusSlot(slot);
            quickBarPills.Children.Add(button);
        }
    }

    private static void ArrangeGrid(
        Grid rightGrid,
        IReadOnlyList<object> visibleSlots,
        bool showMultiChrome,
        Func<object, DockCardChrome> resolveCard,
        Func<object, bool> isFocused)
    {
        var count = visibleSlots.Count;
        if (count == 1)
        {
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PlaceCard(rightGrid, resolveCard(visibleSlots[0]), 0, 0, 1, showMultiChrome, isFocused(visibleSlots[0]));
            return;
        }
        if (count == 2)
        {
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PlaceCard(rightGrid, resolveCard(visibleSlots[0]), 0, 0, 1, showMultiChrome, isFocused(visibleSlots[0]));
            PlaceCard(rightGrid, resolveCard(visibleSlots[1]), 1, 0, 1, showMultiChrome, isFocused(visibleSlots[1]));
            return;
        }
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PlaceCard(rightGrid, resolveCard(visibleSlots[0]), 0, 0, 1, showMultiChrome, isFocused(visibleSlots[0]));
        PlaceCard(rightGrid, resolveCard(visibleSlots[1]), 0, 1, 1, showMultiChrome, isFocused(visibleSlots[1]));
        if (count == 3)
        {
            PlaceCard(rightGrid, resolveCard(visibleSlots[2]), 1, 0, 2, showMultiChrome, isFocused(visibleSlots[2]));
        }
        else
        {
            PlaceCard(rightGrid, resolveCard(visibleSlots[2]), 1, 0, 1, showMultiChrome, isFocused(visibleSlots[2]));
            PlaceCard(rightGrid, resolveCard(visibleSlots[3]), 1, 1, 1, showMultiChrome, isFocused(visibleSlots[3]));
        }
    }

    private static void PlaceCard(Grid grid, DockCardChrome card, int row, int column, int columnSpan, bool showMultiChrome, bool focused)
    {
        Detach(card);
        card.ClearValue(FrameworkElement.MinHeightProperty);
        card.ClearValue(FrameworkElement.HeightProperty);
        card.SetChrome(true, showMultiChrome, focused);
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, columnSpan);
        grid.Children.Add(card);
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
    }
}

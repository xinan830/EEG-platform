using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Materializes a <see cref="DockNode"/> tree into WPF Grids/Splitters/TabGroups while reusing card instances.</summary>
internal sealed class DockTreeRenderer
{
    private readonly List<DockTabGroupView> activeTabGroups = [];
    private readonly List<(string RepresentativePaneId, FrameworkElement Visual, bool AllowCenterTab)> leafVisuals = [];

    public UIElement? Render(
        DockNode? node,
        Func<string, DockCardChrome?> resolvePane,
        Func<string, bool> isPaneFocused,
        Action onSplitRatioChanged,
        Action<string> onTabActivated,
        Action<string, string, Point> onTabDragStarted)
    {
        CleanupVisuals();
        if (node is null) return null;
        return BuildNode(node, resolvePane, isPaneFocused, onSplitRatioChanged, onTabActivated, onTabDragStarted);
    }

    public void CleanupVisuals()
    {
        foreach (var tabGroup in activeTabGroups) tabGroup.ClearPanes();
        activeTabGroups.Clear();
        leafVisuals.Clear();
    }

    public IReadOnlyList<DockTargetRegion> CollectTargetRegions(string? excludeSinglePaneId = null)
    {
        var regions = new List<DockTargetRegion>(leafVisuals.Count);
        foreach (var (repId, visual, allowCenterTab) in leafVisuals)
        {
            if (!visual.IsVisible || visual.ActualWidth <= 4 || visual.ActualHeight <= 4) continue;
            if (visual is DockCardChrome && string.Equals(repId, excludeSinglePaneId, StringComparison.Ordinal)) continue;
            try
            {
                var topLeft = visual.PointToScreen(new Point(0, 0));
                var bottomRight = visual.PointToScreen(new Point(visual.ActualWidth, visual.ActualHeight));
                var bounds = new Rect(topLeft, bottomRight);
                regions.Add(new DockTargetRegion(repId, bounds, allowCenterTab));
            }
            catch
            {
                // Ignore visuals detached mid-frame.
            }
        }
        return regions;
    }

    private UIElement? BuildNode(
        DockNode node,
        Func<string, DockCardChrome?> resolvePane,
        Func<string, bool> isPaneFocused,
        Action onSplitRatioChanged,
        Action<string> onTabActivated,
        Action<string, string, Point> onTabDragStarted)
    {
        switch (node)
        {
            case DockPaneNode pane:
            {
                var chrome = resolvePane(pane.PaneId);
                if (chrome is null) return null;
                DetachFromParent(chrome);
                chrome.ClearValue(FrameworkElement.MinHeightProperty);
                chrome.ClearValue(FrameworkElement.HeightProperty);
                chrome.Visibility = Visibility.Visible;
                chrome.SetChrome(showDragBar: true, showCardBorder: true, isFocused: isPaneFocused(pane.PaneId));
                leafVisuals.Add((pane.PaneId, chrome, true));
                return chrome;
            }

            case DockTabGroupNode tabs:
            {
                var panes = tabs.PaneIds.Select(resolvePane).OfType<DockCardChrome>().ToList();
                if (panes.Count == 0) return null;
                if (panes.Count == 1)
                {
                    var only = panes[0];
                    DetachFromParent(only);
                    only.ClearValue(FrameworkElement.MinHeightProperty);
                    only.ClearValue(FrameworkElement.HeightProperty);
                    only.Visibility = Visibility.Visible;
                    only.SetChrome(showDragBar: true, showCardBorder: true, isFocused: isPaneFocused(only.PaneId));
                    leafVisuals.Add((only.PaneId, only, true));
                    return only;
                }

                var view = new DockTabGroupView();
                view.TabActivated += id =>
                {
                    tabs.ActivePaneId = id;
                    onTabActivated(id);
                };
                view.TabDragStarted += onTabDragStarted;
                view.SetPanes(panes, tabs.ActivePaneId);
                activeTabGroups.Add(view);
                leafVisuals.Add((tabs.ActivePaneId is { Length: > 0 } ? tabs.ActivePaneId : panes[0].PaneId, view, true));
                return view;
            }

            case DockSplitNode split:
            {
                var firstEl = BuildNode(split.First, resolvePane, isPaneFocused, onSplitRatioChanged, onTabActivated, onTabDragStarted);
                var secondEl = BuildNode(split.Second, resolvePane, isPaneFocused, onSplitRatioChanged, onTabActivated, onTabDragStarted);
                if (firstEl is null) return secondEl;
                if (secondEl is null) return firstEl;

                var ratio = DockTreeOperations.ClampRatio(split.FirstRatio);
                var grid = new Grid();
                if (split.Orientation == Orientation.Horizontal)
                {
                    var c1 = new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star), MinWidth = 140 };
                    var cs = new ColumnDefinition { Width = new GridLength(6) };
                    var c2 = new ColumnDefinition { Width = new GridLength(1.0 - ratio, GridUnitType.Star), MinWidth = 140 };
                    grid.ColumnDefinitions.Add(c1);
                    grid.ColumnDefinitions.Add(cs);
                    grid.ColumnDefinitions.Add(c2);

                    var splitter = CreateSplitter(GridResizeDirection.Columns);
                    splitter.DragCompleted += (_, _) =>
                    {
                        var total = c1.ActualWidth + c2.ActualWidth;
                        if (total > 1)
                        {
                            split.FirstRatio = DockTreeOperations.ClampRatio(c1.ActualWidth / total);
                            onSplitRatioChanged();
                        }
                    };

                    Grid.SetColumn(firstEl, 0);
                    Grid.SetColumn(splitter, 1);
                    Grid.SetColumn(secondEl, 2);
                    grid.Children.Add(firstEl);
                    grid.Children.Add(splitter);
                    grid.Children.Add(secondEl);
                }
                else
                {
                    var r1 = new RowDefinition { Height = new GridLength(ratio, GridUnitType.Star), MinHeight = 120 };
                    var rs = new RowDefinition { Height = new GridLength(6) };
                    var r2 = new RowDefinition { Height = new GridLength(1.0 - ratio, GridUnitType.Star), MinHeight = 120 };
                    grid.RowDefinitions.Add(r1);
                    grid.RowDefinitions.Add(rs);
                    grid.RowDefinitions.Add(r2);

                    var splitter = CreateSplitter(GridResizeDirection.Rows);
                    splitter.DragCompleted += (_, _) =>
                    {
                        var total = r1.ActualHeight + r2.ActualHeight;
                        if (total > 1)
                        {
                            split.FirstRatio = DockTreeOperations.ClampRatio(r1.ActualHeight / total);
                            onSplitRatioChanged();
                        }
                    };

                    Grid.SetRow(firstEl, 0);
                    Grid.SetRow(splitter, 1);
                    Grid.SetRow(secondEl, 2);
                    grid.Children.Add(firstEl);
                    grid.Children.Add(splitter);
                    grid.Children.Add(secondEl);
                }
                return grid;
            }

            default:
                return null;
        }
    }

    private static GridSplitter CreateSplitter(GridResizeDirection direction)
    {
        var splitter = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = direction,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
        };
        if (direction == GridResizeDirection.Columns) splitter.Width = 6;
        else splitter.Height = 6;
        return splitter;
    }

    private static void DetachFromParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
    }
}

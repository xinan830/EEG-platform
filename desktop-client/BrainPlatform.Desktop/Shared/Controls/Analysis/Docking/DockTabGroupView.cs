using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>
/// Lightweight tab group keeping all child panes mounted in the visual tree via Visibility switching
/// so switching tabs never unloads WebView2 or SciChart surfaces.
/// </summary>
internal sealed class DockTabGroupView : Border
{
    private readonly Grid root = new();
    private readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly Grid contentGrid = new();
    private readonly Dictionary<string, DockCardChrome> panesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> tabButtons = new(StringComparer.Ordinal);
    private string activePaneId = string.Empty;

    public DockTabGroupView()
    {
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(3);
        ClipToBounds = true;

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 0),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = tabStrip,
            },
        };

        Grid.SetRow(headerBar, 0);
        Grid.SetRow(contentGrid, 1);
        root.Children.Add(headerBar);
        root.Children.Add(contentGrid);
        Child = root;
    }

    public event Action<string>? TabActivated;
    public event Action<string, string, Point>? TabDragStarted;

    public void SetPanes(IReadOnlyList<DockCardChrome> panes, string requestedActiveId)
    {
        tabStrip.Children.Clear();
        contentGrid.Children.Clear();
        panesById.Clear();
        tabButtons.Clear();

        foreach (var pane in panes)
        {
            if (pane.Parent is Panel parentPanel) parentPanel.Children.Remove(pane);
            pane.SetChrome(showDragBar: false, showCardBorder: false, isFocused: true);
            panesById[pane.PaneId] = pane;
            contentGrid.Children.Add(pane);

            var tab = CreateTabButton(pane.PaneId, pane.Title);
            tabButtons[pane.PaneId] = tab;
            tabStrip.Children.Add(tab);
        }

        var nextActive = panesById.ContainsKey(requestedActiveId)
            ? requestedActiveId
            : panes.FirstOrDefault()?.PaneId ?? string.Empty;
        ActivateTab(nextActive, notify: false);
    }

    public void ActivateTab(string paneId, bool notify = true)
    {
        if (!panesById.ContainsKey(paneId)) return;
        activePaneId = paneId;
        foreach (var (id, pane) in panesById)
            pane.Visibility = string.Equals(id, paneId, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (id, tab) in tabButtons)
        {
            var isActive = string.Equals(id, paneId, StringComparison.Ordinal);
            tab.Background = new SolidColorBrush(isActive ? Colors.White : Color.FromRgb(241, 245, 249));
            tab.BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(37, 99, 235) : Colors.Transparent);
            if (tab.Child is TextBlock label)
            {
                label.Foreground = new SolidColorBrush(isActive ? Color.FromRgb(15, 23, 42) : Color.FromRgb(100, 116, 139));
                label.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        if (notify) TabActivated?.Invoke(paneId);
    }

    public string ActivePaneId => activePaneId;

    public void ClearPanes()
    {
        tabStrip.Children.Clear();
        contentGrid.Children.Clear();
        panesById.Clear();
        tabButtons.Clear();
    }

    private Border CreateTabButton(string paneId, string title)
    {
        var label = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 160,
        };
        var tab = new Border
        {
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Cursor = Cursors.Hand,
            Child = label,
            ToolTip = "点击切换选项卡；按住拖动可拆分或独立成窗",
        };

        Point? downPos = null;
        var dragged = false;
        tab.PreviewMouseLeftButtonDown += (_, e) =>
        {
            ActivateTab(paneId);
            downPos = e.GetPosition(tab);
            dragged = false;
        };
        tab.PreviewMouseMove += (_, e) =>
        {
            if (downPos is null || dragged || e.LeftButton != MouseButtonState.Pressed) return;
            var cur = e.GetPosition(tab);
            if (Math.Abs(cur.X - downPos.Value.X) < 8 && Math.Abs(cur.Y - downPos.Value.Y) < 8) return;
            dragged = true;
            downPos = null;
            TabDragStarted?.Invoke(paneId, title, tab.PointToScreen(cur));
        };
        tab.PreviewMouseLeftButtonUp += (_, _) =>
        {
            downPos = null;
            dragged = false;
        };
        return tab;
    }
}

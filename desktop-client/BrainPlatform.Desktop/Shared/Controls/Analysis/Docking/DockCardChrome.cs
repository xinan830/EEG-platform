using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Card container with a slim draggable VS-style title grip that preserves child controls across moves.</summary>
internal sealed class DockCardChrome : Border
{
    private readonly Grid layout = new();
    private readonly Border dragStrip;
    private readonly TextBlock titleLabel;
    private readonly ContentControl contentHost = new();
    private bool mouseDown;
    private Point startPointInStrip;
    private bool dragDispatched;

    public DockCardChrome(string paneId, string title, FrameworkElement? directContent = null, DataTemplate? template = null, object? slotData = null)
    {
        PaneId = paneId;
        Title = title;
        Background = Brushes.White;
        ClipToBounds = true;

        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        titleLabel = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var stripPanel = new DockPanel { LastChildFill = true };
        var grip = CreateGripIcon();
        DockPanel.SetDock(grip, Dock.Left);
        var hint = new TextBlock
        {
            Text = "拖动停靠",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
        };
        DockPanel.SetDock(hint, Dock.Right);
        stripPanel.Children.Add(grip);
        stripPanel.Children.Add(hint);
        stripPanel.Children.Add(titleLabel);

        dragStrip = new Border
        {
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.SizeAll,
            Child = stripPanel,
            ToolTip = "按住并拖动可调整面板位置、合并为标签页或拖出为独立窗口",
        };
        dragStrip.PreviewMouseLeftButtonDown += OnStripMouseDown;
        dragStrip.PreviewMouseMove += OnStripMouseMove;
        dragStrip.PreviewMouseLeftButtonUp += OnStripMouseUp;

        Grid.SetRow(dragStrip, 0);
        layout.Children.Add(dragStrip);

        if (directContent is not null)
        {
            Grid.SetRow(directContent, 1);
            layout.Children.Add(directContent);
        }
        else
        {
            contentHost.Content = slotData;
            contentHost.ContentTemplate = template;
            contentHost.AddHandler(AnalysisPanelShell.HeaderDragStartedEvent, new RoutedEventHandler((_, e) =>
            {
                e.Handled = true;
                Activated?.Invoke(PaneId);
                DragStarted?.Invoke(PaneId, Title, new Point());
            }));
            Grid.SetRow(contentHost, 1);
            layout.Children.Add(contentHost);
        }

        Child = layout;
    }

    public string PaneId { get; }
    public string Title { get; private set; }
    public event Action<string, string, Point>? DragStarted;
    public event Action<string>? Activated;

    public void UpdateTitle(string title)
    {
        Title = title;
        titleLabel.Text = title;
    }

    public void UpdateTemplate(DataTemplate? template)
    {
        contentHost.ContentTemplate = template;
    }

    public void SetChrome(bool showDragBar, bool showCardBorder, bool isFocused)
    {
        var isShellHosted = contentHost.Parent is not null;
        dragStrip.Visibility = showDragBar && !isShellHosted ? Visibility.Visible : Visibility.Collapsed;
        if (isShellHosted) AnalysisPanelShell.SetIsDockDragEnabled(contentHost, showDragBar);
        dragStrip.Background = new SolidColorBrush(isFocused
            ? Color.FromRgb(239, 246, 255)
            : Color.FromRgb(248, 250, 252));
        if (!showCardBorder)
        {
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(0);
            Margin = new Thickness(0);
            return;
        }
        BorderBrush = new SolidColorBrush(isFocused ? Color.FromRgb(96, 165, 250) : Color.FromRgb(226, 232, 240));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(3);
    }

    public void DetachContent(FrameworkElement? directContent)
    {
        if (directContent is not null && layout.Children.Contains(directContent))
            layout.Children.Remove(directContent);
    }

    private void OnStripMouseDown(object sender, MouseButtonEventArgs e)
    {
        Activated?.Invoke(PaneId);
        mouseDown = true;
        dragDispatched = false;
        startPointInStrip = e.GetPosition(dragStrip);
    }

    private void OnStripMouseMove(object sender, MouseEventArgs e)
    {
        if (!mouseDown || dragDispatched || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(dragStrip);
        if (Math.Abs(current.X - startPointInStrip.X) < 8 && Math.Abs(current.Y - startPointInStrip.Y) < 8) return;
        dragDispatched = true;
        mouseDown = false;
        DragStarted?.Invoke(PaneId, Title, dragStrip.PointToScreen(current));
    }

    private void OnStripMouseUp(object sender, MouseButtonEventArgs e)
    {
        mouseDown = false;
        dragDispatched = false;
    }

    private static UIElement CreateGripIcon()
    {
        var canvas = new Canvas { Width = 10, Height = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        var brush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 2; col++)
            {
                var dot = new Ellipse { Width = 2.2, Height = 2.2, Fill = brush };
                Canvas.SetLeft(dot, col * 4.5 + 1);
                Canvas.SetTop(dot, row * 4 + 1);
                canvas.Children.Add(dot);
            }
        }
        return canvas;
    }
}

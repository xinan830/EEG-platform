using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Screen-space hit target representing a visible leaf pane or tab group in the dock tree.</summary>
internal sealed record DockTargetRegion(string RepresentativePaneId, Rect ScreenPixelBounds, bool AllowCenterTab = true);

internal readonly record struct DockDropTarget(string? TargetPaneId, DockDropZone Zone, Rect PreviewDipBounds);

/// <summary>
/// Transparent owned overlay window rendering VS-style docking diamond guides and drop preview boxes
/// above WebView2 and SciChart HWND airspace.
/// </summary>
internal sealed class DockGuideOverlayWindow : Window
{
    private const double GuideSize = 32;
    private const double GuideGap = 6;
    private readonly Canvas canvas = new();
    private readonly Border previewBox = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(46, 37, 99, 235)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(6),
        Visibility = Visibility.Collapsed,
    };
    private readonly Border dragGhostPill = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(238, 15, 23, 42)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(12, 5, 12, 5),
        Visibility = Visibility.Collapsed,
        Child = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
    };

    private readonly Dictionary<DockDropZone, (Border Badge, Rect DipRect)> activeButtons = [];

    public DockGuideOverlayWindow(Window? owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = canvas;
    }

    public DockDropTarget? UpdateGuides(
        Rect hostPixelBounds,
        DpiScale dpi,
        IReadOnlyList<DockTargetRegion> paneRegions,
        Point cursorScreenPx,
        string? dragTitle,
        bool showGhostPill)
    {
        if (hostPixelBounds.Width <= 0 || hostPixelBounds.Height <= 0) return null;

        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        var hostDip = new Rect(
            hostPixelBounds.Left / scaleX,
            hostPixelBounds.Top / scaleY,
            hostPixelBounds.Width / scaleX,
            hostPixelBounds.Height / scaleY);

        Left = hostDip.Left;
        Top = hostDip.Top;
        Width = hostDip.Width;
        Height = hostDip.Height;
        if (!IsVisible) Show();

        canvas.Children.Clear();
        activeButtons.Clear();
        canvas.Children.Add(previewBox);

        var cursorDip = new Point((cursorScreenPx.X - hostPixelBounds.Left) / scaleX, (cursorScreenPx.Y - hostPixelBounds.Top) / scaleY);
        var hostLocalDip = new Rect(0, 0, hostDip.Width, hostDip.Height);

        DockDropTarget? hitTarget = null;
        if (hostPixelBounds.Contains(cursorScreenPx))
        {
            // Outer workspace edge dock buttons
            PlaceGuideButton(DockDropZone.OuterTop, new Point(hostDip.Width / 2, 26), cursorDip, () =>
            {
                hitTarget = new DockDropTarget(null, DockDropZone.OuterTop, new Rect(0, 0, hostDip.Width, hostDip.Height * 0.34));
            });
            PlaceGuideButton(DockDropZone.OuterBottom, new Point(hostDip.Width / 2, hostDip.Height - 26), cursorDip, () =>
            {
                hitTarget = new DockDropTarget(null, DockDropZone.OuterBottom, new Rect(0, hostDip.Height * 0.66, hostDip.Width, hostDip.Height * 0.34));
            });
            PlaceGuideButton(DockDropZone.OuterLeft, new Point(26, hostDip.Height / 2), cursorDip, () =>
            {
                hitTarget = new DockDropTarget(null, DockDropZone.OuterLeft, new Rect(0, 0, hostDip.Width * 0.34, hostDip.Height));
            });
            PlaceGuideButton(DockDropZone.OuterRight, new Point(hostDip.Width - 26, hostDip.Height / 2), cursorDip, () =>
            {
                hitTarget = new DockDropTarget(null, DockDropZone.OuterRight, new Rect(hostDip.Width * 0.66, 0, hostDip.Width * 0.34, hostDip.Height));
            });

            // Hovered target pane center cross diamond
            var hoveredPane = paneRegions.FirstOrDefault(r => r.ScreenPixelBounds.Contains(cursorScreenPx));
            if (hoveredPane is not null)
            {
                var paneDip = new Rect(
                    (hoveredPane.ScreenPixelBounds.Left - hostPixelBounds.Left) / scaleX,
                    (hoveredPane.ScreenPixelBounds.Top - hostPixelBounds.Top) / scaleY,
                    hoveredPane.ScreenPixelBounds.Width / scaleX,
                    hoveredPane.ScreenPixelBounds.Height / scaleY);
                var center = new Point(paneDip.Left + paneDip.Width / 2, paneDip.Top + paneDip.Height / 2);
                var step = GuideSize + GuideGap;

                DrawDiamondBackdrop(center, step);

                if (hoveredPane.AllowCenterTab)
                {
                    PlaceGuideButton(DockDropZone.CenterTab, center, cursorDip, () =>
                        hitTarget = new DockDropTarget(hoveredPane.RepresentativePaneId, DockDropZone.CenterTab, paneDip));
                }
                PlaceGuideButton(DockDropZone.Top, new Point(center.X, center.Y - step), cursorDip, () =>
                    hitTarget = new DockDropTarget(hoveredPane.RepresentativePaneId, DockDropZone.Top,
                        new Rect(paneDip.Left, paneDip.Top, paneDip.Width, paneDip.Height / 2)));
                PlaceGuideButton(DockDropZone.Bottom, new Point(center.X, center.Y + step), cursorDip, () =>
                    hitTarget = new DockDropTarget(hoveredPane.RepresentativePaneId, DockDropZone.Bottom,
                        new Rect(paneDip.Left, paneDip.Top + paneDip.Height / 2, paneDip.Width, paneDip.Height / 2)));
                PlaceGuideButton(DockDropZone.Left, new Point(center.X - step, center.Y), cursorDip, () =>
                    hitTarget = new DockDropTarget(hoveredPane.RepresentativePaneId, DockDropZone.Left,
                        new Rect(paneDip.Left, paneDip.Top, paneDip.Width / 2, paneDip.Height)));
                PlaceGuideButton(DockDropZone.Right, new Point(center.X + step, center.Y), cursorDip, () =>
                    hitTarget = new DockDropTarget(hoveredPane.RepresentativePaneId, DockDropZone.Right,
                        new Rect(paneDip.Left + paneDip.Width / 2, paneDip.Top, paneDip.Width / 2, paneDip.Height)));
            }
        }

        if (hitTarget is { } active)
        {
            var box = active.PreviewDipBounds;
            box.Intersect(hostLocalDip);
            Canvas.SetLeft(previewBox, box.Left + 2);
            Canvas.SetTop(previewBox, box.Top + 2);
            previewBox.Width = Math.Max(0, box.Width - 4);
            previewBox.Height = Math.Max(0, box.Height - 4);
            previewBox.Visibility = Visibility.Visible;
        }
        else
        {
            previewBox.Visibility = Visibility.Collapsed;
        }

        if (showGhostPill && !string.IsNullOrWhiteSpace(dragTitle))
        {
            if (dragGhostPill.Child is TextBlock tb)
            {
                tb.Text = hitTarget is { } t
                    ? $"停靠「{dragTitle}」→ {DescribeZone(t.Zone)}"
                    : $"拖出「{dragTitle}」（松手变为独立窗口）";
            }
            dragGhostPill.Visibility = Visibility.Visible;
            Canvas.SetLeft(dragGhostPill, Math.Clamp(cursorDip.X + 14, 6, Math.Max(6, hostDip.Width - 190)));
            Canvas.SetTop(dragGhostPill, Math.Clamp(cursorDip.Y + 14, 6, Math.Max(6, hostDip.Height - 34)));
            canvas.Children.Add(dragGhostPill);
        }
        else
        {
            dragGhostPill.Visibility = Visibility.Collapsed;
        }

        return hitTarget;
    }

    private void DrawDiamondBackdrop(Point center, double step)
    {
        var span = step * 2 + GuideSize + 8;
        var cross = new Border
        {
            Width = span,
            Height = span,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(18, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 15, 23, 42)),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(cross, center.X - span / 2);
        Canvas.SetTop(cross, center.Y - span / 2);
        canvas.Children.Add(cross);
    }

    private void PlaceGuideButton(DockDropZone zone, Point center, Point cursorDip, Action onHover)
    {
        var rect = new Rect(center.X - GuideSize / 2, center.Y - GuideSize / 2, GuideSize, GuideSize);
        var hovered = rect.Contains(cursorDip);
        if (hovered) onHover();

        var badge = new Border
        {
            Width = GuideSize,
            Height = GuideSize,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(hovered ? Color.FromRgb(37, 99, 235) : Color.FromArgb(242, 30, 41, 59)),
            BorderBrush = new SolidColorBrush(hovered ? Color.FromRgb(147, 197, 253) : Color.FromRgb(100, 116, 139)),
            BorderThickness = new Thickness(1.4),
            Child = BuildZoneGlyph(zone, hovered),
        };
        Canvas.SetLeft(badge, rect.Left);
        Canvas.SetTop(badge, rect.Top);
        canvas.Children.Add(badge);
        activeButtons[zone] = (badge, rect);
    }

    private static UIElement BuildZoneGlyph(DockDropZone zone, bool hovered)
    {
        var stroke = new SolidColorBrush(hovered ? Colors.White : Color.FromRgb(226, 232, 240));
        var accent = new SolidColorBrush(hovered ? Colors.White : Color.FromRgb(96, 165, 250));
        var g = new Grid { Width = 18, Height = 18 };
        g.Children.Add(new Rectangle
        {
            Width = 16, Height = 16, Stroke = stroke, StrokeThickness = 1.2, RadiusX = 2, RadiusY = 2,
        });

        var dockRect = new Rectangle { Fill = accent };
        switch (zone)
        {
            case DockDropZone.CenterTab:
                dockRect.Width = 10; dockRect.Height = 10;
                break;
            case DockDropZone.Left or DockDropZone.OuterLeft:
                dockRect.Width = 6; dockRect.Height = 12; dockRect.HorizontalAlignment = HorizontalAlignment.Left; dockRect.Margin = new Thickness(2, 0, 0, 0);
                break;
            case DockDropZone.Right or DockDropZone.OuterRight:
                dockRect.Width = 6; dockRect.Height = 12; dockRect.HorizontalAlignment = HorizontalAlignment.Right; dockRect.Margin = new Thickness(0, 0, 2, 0);
                break;
            case DockDropZone.Top or DockDropZone.OuterTop:
                dockRect.Width = 12; dockRect.Height = 6; dockRect.VerticalAlignment = VerticalAlignment.Top; dockRect.Margin = new Thickness(0, 2, 0, 0);
                break;
            case DockDropZone.Bottom or DockDropZone.OuterBottom:
                dockRect.Width = 12; dockRect.Height = 6; dockRect.VerticalAlignment = VerticalAlignment.Bottom; dockRect.Margin = new Thickness(0, 0, 0, 2);
                break;
        }
        g.Children.Add(dockRect);
        return g;
    }

    private static string DescribeZone(DockDropZone zone) => zone switch
    {
        DockDropZone.CenterTab => "合并为标签页",
        DockDropZone.Left => "左侧分栏",
        DockDropZone.Right => "右侧分栏",
        DockDropZone.Top => "上方分栏",
        DockDropZone.Bottom => "下方分栏",
        DockDropZone.OuterLeft => "最左侧整栏",
        DockDropZone.OuterRight => "最右侧整栏",
        DockDropZone.OuterTop => "最顶部整栏",
        DockDropZone.OuterBottom => "最底部整栏",
        _ => "停靠",
    };
}

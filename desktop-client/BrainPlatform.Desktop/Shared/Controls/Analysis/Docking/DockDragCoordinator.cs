using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>
/// Coordinates VS-style drag operations for both docked cards/tabs and floating windows being dragged back.
/// </summary>
internal sealed class DockDragCoordinator
{
    private readonly FrameworkElement hostElement;
    private readonly Func<string?, IReadOnlyList<DockTargetRegion>> collectRegions;
    private readonly Action<string, string?, DockDropZone> onDropDock;
    private readonly Action<string, Point> onDropFloat;

    private DockGuideOverlayWindow? overlayWindow;
    private DockDropTarget? activeDropTarget;
    private string? activePaneId;
    private string? activeTitle;
    private bool isDockedDragging;

    private DispatcherTimer? floatingPollTimer;
    private Window? trackedFloatingWindow;
    private string? trackedFloatingPaneId;
    private string? trackedFloatingTitle;

    public DockDragCoordinator(
        FrameworkElement hostElement,
        Func<string?, IReadOnlyList<DockTargetRegion>> collectRegions,
        Action<string, string?, DockDropZone> onDropDock,
        Action<string, Point> onDropFloat)
    {
        this.hostElement = hostElement;
        this.collectRegions = collectRegions;
        this.onDropDock = onDropDock;
        this.onDropFloat = onDropFloat;

        hostElement.PreviewMouseMove += OnHostMouseMove;
        hostElement.PreviewMouseLeftButtonUp += OnHostMouseUp;
        hostElement.LostMouseCapture += (_, _) => CancelDockedDrag();
        hostElement.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && isDockedDragging) CancelDockedDrag();
        };
    }

    public void BeginDockedDrag(string paneId, string title)
    {
        if (isDockedDragging || !hostElement.IsVisible) return;
        activePaneId = paneId;
        activeTitle = title;
        isDockedDragging = true;
        activeDropTarget = null;
        hostElement.CaptureMouse();
        UpdateOverlayForCurrentCursor(showGhostPill: true);
    }

    public void AttachFloatingWindow(Window window, string paneId, string title)
    {
        window.LocationChanged += (_, _) =>
        {
            if (isDockedDragging || !window.IsLoaded || !window.IsVisible) return;
            if (!IsLeftMouseButtonDown()) return;
            StartTrackingFloatingDrag(window, paneId, title);
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(trackedFloatingWindow, window)) StopTrackingFloatingDrag(commitDrop: false);
        };
    }

    public void Dispose()
    {
        CancelDockedDrag();
        StopTrackingFloatingDrag(commitDrop: false);
        overlayWindow?.Close();
        overlayWindow = null;
    }

    private void OnHostMouseMove(object sender, MouseEventArgs e)
    {
        if (!isDockedDragging) return;
        UpdateOverlayForCurrentCursor(showGhostPill: true);
    }

    private void OnHostMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDockedDragging) return;
        var paneId = activePaneId;
        var drop = activeDropTarget;
        var cursor = GetCursorScreenPoint();
        var hostBounds = GetHostPixelBounds();

        CancelDockedDrag();
        if (string.IsNullOrWhiteSpace(paneId)) return;

        if (drop is { } target)
        {
            onDropDock(paneId, target.TargetPaneId, target.Zone);
            return;
        }

        if (!string.Equals(paneId, DockTreeOperations.WaveformPaneId, StringComparison.Ordinal) &&
            !hostBounds.Contains(cursor))
        {
            onDropFloat(paneId, cursor);
        }
    }

    private void CancelDockedDrag()
    {
        if (!isDockedDragging) return;
        isDockedDragging = false;
        activePaneId = null;
        activeTitle = null;
        activeDropTarget = null;
        if (hostElement.IsMouseCaptured) hostElement.ReleaseMouseCapture();
        HideOverlay();
    }

    private void StartTrackingFloatingDrag(Window window, string paneId, string title)
    {
        trackedFloatingWindow = window;
        trackedFloatingPaneId = paneId;
        trackedFloatingTitle = title;
        if (floatingPollTimer is null)
        {
            floatingPollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(24) };
            floatingPollTimer.Tick += (_, _) => OnFloatingPollTick();
        }
        if (!floatingPollTimer.IsEnabled) floatingPollTimer.Start();
        OnFloatingPollTick();
    }

    private void OnFloatingPollTick()
    {
        if (trackedFloatingWindow is null || !hostElement.IsVisible)
        {
            StopTrackingFloatingDrag(commitDrop: false);
            return;
        }
        if (!IsLeftMouseButtonDown())
        {
            StopTrackingFloatingDrag(commitDrop: true);
            return;
        }

        var cursor = GetCursorScreenPoint();
        var hostBounds = GetHostPixelBounds();
        if (hostBounds.Contains(cursor))
        {
            activePaneId = trackedFloatingPaneId;
            activeTitle = trackedFloatingTitle;
            UpdateOverlayForCurrentCursor(showGhostPill: false);
        }
        else
        {
            activeDropTarget = null;
            HideOverlay();
        }
    }

    private void StopTrackingFloatingDrag(bool commitDrop)
    {
        floatingPollTimer?.Stop();
        var paneId = trackedFloatingPaneId;
        var drop = activeDropTarget;
        trackedFloatingWindow = null;
        trackedFloatingPaneId = null;
        trackedFloatingTitle = null;
        activePaneId = null;
        activeTitle = null;
        activeDropTarget = null;
        HideOverlay();

        if (commitDrop && !string.IsNullOrWhiteSpace(paneId) && drop is { } target)
        {
            onDropDock(paneId, target.TargetPaneId, target.Zone);
        }
    }

    private void UpdateOverlayForCurrentCursor(bool showGhostPill)
    {
        var hostBounds = GetHostPixelBounds();
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            activeDropTarget = null;
            return;
        }
        overlayWindow ??= new DockGuideOverlayWindow(Window.GetWindow(hostElement));
        var dpi = VisualTreeHelper.GetDpi(hostElement);
        var regions = collectRegions(activePaneId);
        var cursor = GetCursorScreenPoint();
        activeDropTarget = overlayWindow.UpdateGuides(hostBounds, dpi, regions, cursor, activeTitle, showGhostPill);
    }

    private void HideOverlay()
    {
        if (overlayWindow is { IsVisible: true }) overlayWindow.Hide();
    }

    private Rect GetHostPixelBounds()
    {
        if (!hostElement.IsLoaded || hostElement.ActualWidth <= 0 || hostElement.ActualHeight <= 0) return Rect.Empty;
        var tl = hostElement.PointToScreen(new Point(0, 0));
        var br = hostElement.PointToScreen(new Point(hostElement.ActualWidth, hostElement.ActualHeight));
        return new Rect(tl, br);
    }

    private static Point GetCursorScreenPoint() =>
        NativeMethods.GetCursorPos(out var pt) ? new Point(pt.X, pt.Y) : new Point();

    private static bool IsLeftMouseButtonDown() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VkLbutton) & 0x8000) != 0;

    private static class NativeMethods
    {
        public const int VkLbutton = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        public struct Win32Point { public int X; public int Y; }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Win32Point lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Compact review navigator. Thumb dragging is rate-limited to the review
/// loader's usable range; clicking the track requests an atomic target jump.
/// </summary>
public sealed class ReviewTimelineNavigator : FrameworkElement
{
    public static readonly DependencyProperty DurationSecondsProperty =
        DependencyProperty.Register(nameof(DurationSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportStartSecondsProperty =
        DependencyProperty.Register(nameof(ViewportStartSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VisibleDurationSecondsProperty =
        DependencyProperty.Register(nameof(VisibleDurationSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double DragPagesPerSecond = 2d;
    private bool dragging;
    private double dragOffset;
    private double thumbGrabRatio;
    private double lastAcceptedPosition;
    private DateTime lastDragTimeUtc;

    public event EventHandler<double>? PreviewPositionChanged;
    public event EventHandler<double>? PositionCommitted;

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double ViewportStartSeconds
    {
        get => (double)GetValue(ViewportStartSecondsProperty);
        set => SetValue(ViewportStartSecondsProperty, value);
    }

    public double VisibleDurationSeconds
    {
        get => (double)GetValue(VisibleDurationSecondsProperty);
        set => SetValue(VisibleDurationSecondsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var trackY = Math.Max(1, height - 3);
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2);
        drawingContext.DrawLine(linePen, new Point(0, trackY), new Point(width, trackY));

        var thumb = GetThumbRect();
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(128, 131, 136)),
            null,
            thumb);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        var thumb = GetThumbRect();
        if (!thumb.Contains(point))
        {
            // Do not move the visible thumb here. The receiver first prepares
            // the target frame, then its binding commits the new viewport.
            PositionCommitted?.Invoke(this, GetPositionForTrackX(point.X));
            e.Handled = true;
            return;
        }

        dragging = true;
        dragOffset = point.X - thumb.X;
        thumbGrabRatio = thumb.Width <= 0 ? 0.5 : Math.Clamp(dragOffset / thumb.Width, 0, 1);
        lastAcceptedPosition = GetPlayheadPosition(ViewportStartSeconds);
        lastDragTimeUtc = DateTime.UtcNow;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var requestedPosition = GetPositionForThumbX(e.GetPosition(this).X - dragOffset);
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max(0, (now - lastDragTimeUtc).TotalSeconds);
        var visible = GetVisibleDuration();
        var maximumOffset = elapsedSeconds * DragPagesPerSecond * visible;
        var acceptedPosition = lastAcceptedPosition + Math.Clamp(
            requestedPosition - lastAcceptedPosition,
            -maximumOffset,
            maximumOffset);
        acceptedPosition = Math.Clamp(acceptedPosition, 0, Math.Max(0, DurationSeconds));

        var wasLimited = Math.Abs(acceptedPosition - requestedPosition) > 0.000_001;
        lastAcceptedPosition = acceptedPosition;
        lastDragTimeUtc = now;
        PreviewPositionChanged?.Invoke(this, acceptedPosition);

        if (wasLimited)
        {
            // Keep the pointer over the same place inside the thumb. Without
            // this, cursor and waveform navigator diverge during a long drag.
            MoveCursorToAcceptedThumb(acceptedPosition);
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging)
        {
            return;
        }

        dragging = false;
        ReleaseMouseCapture();
        // The accepted drag updates have already started the newest loader;
        // emitting an unbounded final jump would reintroduce stale waveforms.
        e.Handled = true;
    }

    private Rect GetThumbRect()
    {
        var duration = Math.Max(0.001, DurationSeconds);
        var visible = GetVisibleDuration();
        var width = Math.Clamp(Math.Max(18, ActualWidth * visible / duration), 0, ActualWidth);
        var maxStart = Math.Max(0, duration - visible);
        var start = Math.Clamp(ViewportStartSeconds, 0, maxStart);
        var x = maxStart <= 0 ? 0 : (ActualWidth - width) * start / maxStart;
        return new Rect(x, 0, width, Math.Max(20, ActualHeight - 5));
    }

    private double GetPositionForThumbX(double thumbX)
    {
        var duration = Math.Max(0.001, DurationSeconds);
        var visible = GetVisibleDuration();
        var thumbWidth = Math.Clamp(Math.Max(18, ActualWidth * visible / duration), 0, ActualWidth);
        var maxX = Math.Max(0, ActualWidth - thumbWidth);
        var fraction = maxX <= 0 ? 0 : Math.Clamp(thumbX / maxX, 0, 1);
        var maxStart = Math.Max(0, duration - visible);
        var viewportStart = fraction * maxStart;
        return GetPlayheadPosition(viewportStart);
    }

    private double GetPositionForTrackX(double trackX)
    {
        var visible = GetVisibleDuration();
        var maxStart = Math.Max(0, DurationSeconds - visible);
        var fraction = ActualWidth <= 0 ? 0 : Math.Clamp(trackX / ActualWidth, 0, 1);
        return GetPlayheadPosition(fraction * maxStart);
    }

    private double GetPlayheadPosition(double viewportStart) => Math.Min(
        Math.Max(0, DurationSeconds),
        viewportStart + GetVisibleDuration() * 0.25);

    private double GetVisibleDuration() => Math.Clamp(
        VisibleDurationSeconds,
        0.001,
        Math.Max(0.001, DurationSeconds));

    private void MoveCursorToAcceptedThumb(double acceptedPosition)
    {
        var visible = GetVisibleDuration();
        var maxStart = Math.Max(0, DurationSeconds - visible);
        var viewportStart = Math.Clamp(acceptedPosition - visible * 0.25, 0, maxStart);
        var duration = Math.Max(0.001, DurationSeconds);
        var thumbWidth = Math.Clamp(Math.Max(18, ActualWidth * visible / duration), 0, ActualWidth);
        var thumbX = maxStart <= 0 ? 0 : (ActualWidth - thumbWidth) * viewportStart / maxStart;
        var point = PointToScreen(new Point(thumbX + thumbWidth * thumbGrabRatio, ActualHeight / 2));
        SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
}

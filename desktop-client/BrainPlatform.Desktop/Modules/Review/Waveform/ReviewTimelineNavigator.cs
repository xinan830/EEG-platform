using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>
/// Compact review navigator. Thumb dragging is rate-limited to the review
/// loader's usable range; clicking the track requests an atomic target jump.
/// </summary>
public sealed class ReviewTimelineNavigator : FrameworkElement
{
    public static readonly DependencyProperty RecordingStartUtcProperty =
        DependencyProperty.Register(nameof(RecordingStartUtc), typeof(DateTimeOffset), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationSecondsProperty =
        DependencyProperty.Register(nameof(DurationSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeDisplayModeProperty =
        DependencyProperty.Register(nameof(TimeDisplayMode), typeof(RecordingTimeDisplayMode), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(RecordingTimeDisplayMode.Relative, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeDisplayProperty =
        DependencyProperty.Register(nameof(TimeDisplay), typeof(RecordingTimeDisplayContext), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionSecondsProperty =
        DependencyProperty.Register(nameof(PositionSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VisibleDurationSecondsProperty =
        DependencyProperty.Register(nameof(VisibleDurationSeconds), typeof(double), typeof(ReviewTimelineNavigator),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double DragPagesPerSecond = 2d;
    private const double DragStartThresholdDips = 3d;
    private const double ThumbHeight = 12d;
    private const double ThumbWidth = 18d;
    private bool dragging;
    private bool hasDragged;
    private Point pointerDownPoint;
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

    public RecordingTimeDisplayMode TimeDisplayMode
    {
        get => (RecordingTimeDisplayMode)GetValue(TimeDisplayModeProperty);
        set => SetValue(TimeDisplayModeProperty, value);
    }

    public RecordingTimeDisplayContext? TimeDisplay
    {
        get => (RecordingTimeDisplayContext?)GetValue(TimeDisplayProperty);
        set => SetValue(TimeDisplayProperty, value);
    }

    public DateTimeOffset RecordingStartUtc
    {
        get => (DateTimeOffset)GetValue(RecordingStartUtcProperty);
        set => SetValue(RecordingStartUtcProperty, value);
    }

    public double PositionSeconds
    {
        get => (double)GetValue(PositionSecondsProperty);
        set => SetValue(PositionSecondsProperty, value);
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

        // A transparent drawing makes the entire white track hit-testable.
        // Without it, WPF only routes mouse events over the painted thumb and
        // blue line, so clicks in the visible white space never reach us.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var trackY = Math.Max(1, height - 3);
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2);
        drawingContext.DrawLine(linePen, new Point(0, trackY), new Point(width, trackY));

        var thumb = GetThumbRect();
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(128, 131, 136)),
            null,
            thumb);

        if (height >= 28)
        {
            DrawTimeLabels(drawingContext, width);
        }
    }

    private void DrawTimeLabels(DrawingContext drawingContext, double width)
    {
        var positions = width >= 400 ? new[] { 0d, 0.5d, 1d } : new[] { 0d, 1d };
        var previousRight = double.NegativeInfinity;
        foreach (var fraction in positions)
        {
            var seconds = fraction * DurationSeconds;
            var label = TimeDisplay is { } context
                ? context.IsClockTime ? context.FormatAxis(seconds) : context.FormatPoint(seconds)
                : TimeDisplayMode == RecordingTimeDisplayMode.Clock && RecordingStartUtc != default
                    ? RecordingClockLabelFormatter.Format(RecordingStartUtc, seconds)
                    : seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
            var text = new FormattedText(
                label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var x = Math.Clamp(width * fraction - text.Width / 2, 0, Math.Max(0, width - text.Width));
            if (text.Width > width || x < previousRight + 8) continue;
            drawingContext.DrawText(text, new Point(x, 0));
            previousRight = x + text.Width;
        }
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
            if (!IsBlueTrackHit(point))
            {
                // The unoccupied white track is the deliberate jump target.
                // The receiver prepares the complete frame before it moves
                // the visible viewport binding.
                PositionCommitted?.Invoke(this, GetPositionForTrackX(point.X));
            }

            e.Handled = true;
            return;
        }

        BeginDrag(point, thumb);
        CaptureMouse();
        e.Handled = true;
    }

    private void BeginDrag(Point point, Rect thumb)
    {
        dragging = true;
        hasDragged = false;
        pointerDownPoint = point;
        dragOffset = point.X - thumb.X;
        thumbGrabRatio = thumb.Width <= 0 ? 0.5 : Math.Clamp(dragOffset / thumb.Width, 0, 1);
        lastAcceptedPosition = Math.Clamp(PositionSeconds, 0, Math.Max(0, DurationSeconds));
        lastDragTimeUtc = DateTime.UtcNow;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!hasDragged && Math.Abs(point.X - pointerDownPoint.X) < DragStartThresholdDips)
        {
            return;
        }

        hasDragged = true;
        var requestedPosition = GetPositionForThumbX(point.X - dragOffset);
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
        // A real drag has already started the newest bounded loader. Clicking
        // the thumb itself intentionally leaves the viewport in place.
        e.Handled = true;
    }

    private Rect GetThumbRect()
    {
        var duration = Math.Max(0.001, DurationSeconds);
        var width = GetThumbWidth();
        var x = Math.Max(0, ActualWidth - width) * Math.Clamp(PositionSeconds / duration, 0, 1);
        var y = Math.Max(0, ActualHeight - ThumbHeight - 3d);
        return new Rect(x, y, width, Math.Min(ThumbHeight, ActualHeight));
    }

    private double GetPositionForThumbX(double thumbX)
    {
        var duration = Math.Max(0.001, DurationSeconds);
        var thumbWidth = GetThumbWidth();
        var maxX = Math.Max(0, ActualWidth - thumbWidth);
        var fraction = maxX <= 0 ? 0 : Math.Clamp(thumbX / maxX, 0, 1);
        return fraction * duration;
    }

    private double GetPositionForTrackX(double trackX)
    {
        return GetPositionForThumbX(trackX - GetThumbWidth() / 2);
    }

    // The blue indicator is rendered at height - 3 with a 2px pen. Keep its
    // hit area equally narrow so the surrounding white track remains a large,
    // reliable jump target.
    private bool IsBlueTrackHit(Point point) =>
        Math.Abs(point.Y - Math.Max(1, ActualHeight - 3)) <= 1d;

    private double GetThumbWidth() => Math.Clamp(ThumbWidth, 0, Math.Max(0, ActualWidth));

    private double GetVisibleDuration() => Math.Clamp(
        VisibleDurationSeconds,
        0.001,
        Math.Max(0.001, DurationSeconds));

    private void MoveCursorToAcceptedThumb(double acceptedPosition)
    {
        var duration = Math.Max(0.001, DurationSeconds);
        var thumbWidth = GetThumbWidth();
        var thumbX = Math.Max(0, ActualWidth - thumbWidth) * Math.Clamp(acceptedPosition / duration, 0, 1);
        var point = PointToScreen(new Point(thumbX + thumbWidth * thumbGrabRatio, ActualHeight / 2));
        SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
}

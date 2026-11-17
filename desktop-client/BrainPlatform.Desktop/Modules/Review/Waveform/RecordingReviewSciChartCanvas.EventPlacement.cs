using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

public sealed partial class RecordingReviewSciChartCanvas
{
    private readonly Canvas eventPreviewLayer = new() { IsHitTestVisible = false };
    private readonly Line eventPreviewLine = new() { StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2 } };
    private readonly TextBlock eventPreviewLabel = new() { Background = Brushes.White, FontSize = 11, Padding = new Thickness(4, 2, 4, 2) };
    private RecordingEvent? pressedEvent;
    private Point eventDragOrigin;
    private double eventDragOffsetSeconds;
    private bool isEventDragging;
    private bool eventPointerCommitPending;

    private void InitializeEventPlacement()
    {
        eventPreviewLayer.Children.Add(eventPreviewLine);
        eventPreviewLayer.Children.Add(eventPreviewLabel);
        chartHost.Children.Add(eventPreviewLayer);
        chartHost.PreviewMouseLeftButtonDown += OnEventPointerDown;
        chartHost.PreviewMouseLeftButtonUp += OnEventPointerUp;
        chartHost.PreviewMouseMove += OnEventPointerMove;
        chartHost.MouseLeave += (_, _) =>
        {
            if (!isEventDragging) (DataContext as RecordingReviewViewModel)?.HideEventPreview();
        };
        chartHost.LostMouseCapture += (_, _) =>
        {
            if (isEventDragging) (DataContext as RecordingReviewViewModel)?.CancelEventPlacement();
            pressedEvent = null;
            isEventDragging = false;
        };
        surface.LayoutUpdated += (_, _) => UpdateEventPlacementPreview();
        UpdateEventPlacementPreview();
    }

    private Rect EventPlotArea()
    {
        var area = WaveformPlotLayout.GetPlotArea(surface);
        return new Rect(surface.TranslatePoint(area.TopLeft, chartHost), area.Size);
    }

    internal bool PreviewEventAtPoint(Point point)
    {
        if (DataContext is not RecordingReviewViewModel model || model.CurrentFrame is null ||
            xAxis.VisibleRange is not DoubleRange range) return false;
        if (ReviewEventPlotCoordinates.TryGetSeconds(EventPlotArea(), point, range.Min, range.Max, out var seconds))
            return model.PreviewEventAtSeconds(seconds - (isEventDragging ? eventDragOffsetSeconds : 0));
        model.HideEventPreview();
        return false;
    }

    internal bool BeginEventMarkerDrag(RecordingEvent item, Point point)
    {
        if (DataContext is not RecordingReviewViewModel model || model.IsEventSaving || !item.IsDisplayable ||
            item.Source is not (EventSource.ManualButton or EventSource.KeyboardShortcut)) return false;
        model.SelectedRecordingEvent = item;
        if (model.IsPlaying) model.TogglePlayback();
        pressedEvent = item;
        eventDragOrigin = point;
        eventDragOffsetSeconds = xAxis.VisibleRange is DoubleRange range &&
            ReviewEventPlotCoordinates.TryGetSeconds(EventPlotArea(), point, range.Min, range.Max, out var seconds)
            ? seconds - item.StartSample / (double)model.SamplingRateHz : 0;
        return true;
    }

    internal bool MoveEventMarkerDrag(Point point)
    {
        if (pressedEvent is null || DataContext is not RecordingReviewViewModel model) return false;
        if (!isEventDragging)
        {
            if (Math.Abs(point.X - eventDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - eventDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
            model.BeginEventPlacement(true, pressedEvent);
            isEventDragging = true;
        }
        return PreviewEventAtPoint(point);
    }

    internal async Task EndEventMarkerDragAsync(Point point)
    {
        var shouldCommit = isEventDragging && PreviewEventAtPoint(point);
        var model = DataContext as RecordingReviewViewModel;
        pressedEvent = null;
        isEventDragging = false;
        if (chartHost.IsMouseCaptured) chartHost.ReleaseMouseCapture();
        if (model is null) return;
        if (shouldCommit) await model.CommitEventPlacementAsync();
        else model.CancelEventPlacement();
    }

    private async void OnEventPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel model || eventPointerCommitPending) return;
        var point = e.GetPosition(chartHost);
        if (!EventPlotArea().Contains(point)) return;
        var target = FindEventTarget(e.OriginalSource as DependencyObject);
        if (model.EventPlacementMode == ReviewEventPlacementMode.Move && target is not null &&
            target.Id == model.SelectedRecordingEvent?.Id && BeginEventMarkerDrag(target, point))
        {
            e.Handled = true;
            chartHost.CaptureMouse();
            return;
        }
        if (model.IsEventPlacementActive)
        {
            e.Handled = true;
            if (!PreviewEventAtPoint(point)) return;
            eventPointerCommitPending = true;
            try { await model.CommitEventPlacementAsync(); }
            catch (Exception exception) { model.ReportEventShortcutError(exception); }
            finally { eventPointerCommitPending = false; }
            return;
        }
        if (target is not { } item) return;
        e.Handled = true;
        model.SelectedRecordingEvent = item;
        if (BeginEventMarkerDrag(item, point)) chartHost.CaptureMouse();
    }

    private void OnEventPointerMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel model || eventPointerCommitPending) return;
        try
        {
            if (pressedEvent is not null && e.LeftButton == MouseButtonState.Pressed)
            {
                MoveEventMarkerDrag(e.GetPosition(chartHost));
                e.Handled = true;
            }
            else if (model.IsEventPlacementActive) PreviewEventAtPoint(e.GetPosition(chartHost));
        }
        catch (Exception exception) { CancelEventPointer(); model.ReportEventShortcutError(exception); }
    }

    private async void OnEventPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (pressedEvent is null || eventPointerCommitPending) return;
        e.Handled = true;
        eventPointerCommitPending = true;
        try { await EndEventMarkerDragAsync(e.GetPosition(chartHost)); }
        catch (Exception exception) { (DataContext as RecordingReviewViewModel)?.ReportEventShortcutError(exception); }
        finally { eventPointerCommitPending = false; }
    }

    private static RecordingEvent? FindEventTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: RecordingEvent item }) return item;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void CancelEventPointer()
    {
        pressedEvent = null;
        isEventDragging = false;
        if (chartHost.IsMouseCaptured) chartHost.ReleaseMouseCapture();
        (DataContext as RecordingReviewViewModel)?.CancelEventPlacement();
    }

    private void UpdateEventPlacementPreview()
    {
        var model = DataContext as RecordingReviewViewModel;
        if (model?.IsEventPlacementActive != true)
        {
            eventPreviewLayer.Visibility = Visibility.Collapsed;
            chartHost.ClearValue(CursorProperty);
            if (isEventDragging) CancelEventPointer();
            return;
        }
        chartHost.Cursor = Cursors.Cross;
        if (model.EventPreviewSample is not { } sample || xAxis.VisibleRange is not DoubleRange range || range.Max <= range.Min)
        {
            eventPreviewLayer.Visibility = Visibility.Collapsed;
            return;
        }
        var seconds = sample / (double)model.SamplingRateHz;
        var plot = EventPlotArea();
        if (seconds < range.Min || seconds > range.Max) { eventPreviewLayer.Visibility = Visibility.Collapsed; return; }
        var x = plot.Left + (seconds - range.Min) / (range.Max - range.Min) * plot.Width;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(model.EventPlacementColor));
        eventPreviewLine.Stroke = brush;
        eventPreviewLine.X1 = eventPreviewLine.X2 = x;
        eventPreviewLine.Y1 = plot.Top;
        eventPreviewLine.Y2 = plot.Bottom;
        eventPreviewLabel.Text = $"{model.EventPreviewTimeText} · {(isEventDragging ? "松开确认" : "点击确认")}";
        eventPreviewLabel.Foreground = brush;
        WaveformEventTimeLabel.Position(eventPreviewLayer, eventPreviewLabel, x, plot.Top + 20);
        eventPreviewLayer.Visibility = Visibility.Visible;
    }
}

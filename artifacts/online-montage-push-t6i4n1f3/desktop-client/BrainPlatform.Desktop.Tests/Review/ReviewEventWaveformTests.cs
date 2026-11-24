using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainPlatform.Desktop.Tests.Shared;
using SciChart.Charting.Visuals.Axes;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public Task EventWaveformUsesVisibleAxisAndMatchingColoredPreview() => RunEventUiAsync(async () =>
    {
        await using var context = await EventContext.CreateAsync();
        await context.Model.InitializeAsync();
        var chart = new RecordingReviewSciChartCanvas { DataContext = context.Model };
        var window = HostEventChart(chart);
        try
        {
            EventField<NumericAxis>(chart, "xAxis").VisibleRange = new DoubleRange(5, 15);
            context.Model.BeginEventPlacement(false);
            Assert.True(chart.PreviewEventAtPoint(EventPoint(chart, 8, 5, 15)));
            Assert.Equal(4000, context.Model.EventPreviewSample);
            var line = EventField<Line>(chart, "eventPreviewLine");
            Assert.Equal(Color.FromRgb(192, 64, 160), Assert.IsType<SolidColorBrush>(line.Stroke).Color);
            Assert.Contains("00:00:08.000", EventField<TextBlock>(chart, "eventPreviewLabel").Text);
            Assert.Equal(Visibility.Visible, EventField<Canvas>(chart, "eventPreviewLayer").Visibility);
            Assert.True(chart.PreviewEventAtPoint(EventPoint(chart, 5, 5, 15)));
            Assert.Equal(2500, context.Model.EventPreviewSample);
            Assert.False(chart.PreviewEventAtPoint(new Point(-1, -1)));
            Assert.Null(context.Model.EventPreviewSample);
            Assert.True(chart.PreviewEventAtPoint(EventPoint(chart, 8, 5, 15)));
            await context.Model.CommitEventPlacementAsync();
            Assert.Equal(4000, Assert.Single(context.Model.RecordingEvents).StartSample);
            Assert.Equal(Visibility.Collapsed, EventField<Canvas>(chart, "eventPreviewLayer").Visibility);
            var markers = EventField<ReviewEventMarkerLayer>(chart, "eventMarkers");
            Assert.All(markers.Canvas.Children.OfType<FrameworkElement>(), marker =>
                Assert.IsType<RecordingEvent>(marker.Tag));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EventWaveformDragCommitsOnReleaseAndKeepsIntervalGrabOffset() => RunEventUiAsync(async () =>
    {
        await using var context = await EventContext.CreateAsync();
        var item = await context.CreateIntervalAsync();
        await context.Model.InitializeAsync();
        var chart = new RecordingReviewSciChartCanvas { DataContext = context.Model };
        var window = HostEventChart(chart);
        try
        {
            EventField<NumericAxis>(chart, "xAxis").VisibleRange = new DoubleRange(0, 20);
            var grabSeconds = item.StartSample / 500d + 0.3;
            var origin = EventPoint(chart, grabSeconds, 0, 20);
            Assert.True(chart.BeginEventMarkerDrag(item, origin));
            Assert.False(chart.MoveEventMarkerDrag(new Point(origin.X + 1, origin.Y)));
            Assert.False(context.Model.IsEventPlacementActive);
            var target = EventPoint(chart, grabSeconds + 2, 0, 20);
            Assert.True(chart.MoveEventMarkerDrag(target));
            Assert.Equal(2001, context.Model.EventPreviewSample);
            Assert.Equal(item, Assert.Single(context.Model.RecordingEvents));
            await chart.EndEventMarkerDragAsync(target);
            var moved = Assert.Single(context.Model.RecordingEvents);
            Assert.Equal(2001, moved.StartSample);
            Assert.Equal(item.DurationSamples, moved.DurationSamples);
            Assert.Equal(item.Note, moved.Note);
            Assert.Equal(item.DefinitionSnapshot, moved.DefinitionSnapshot);
            Assert.False(context.Model.IsEventPlacementActive);
            Assert.False(EventField<Grid>(chart, "chartHost").IsMouseCaptured);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EventWaveformOutsideReleaseLostCaptureAndUnloadCancelWithoutWriting() => RunEventUiAsync(async () =>
    {
        await using var context = await EventContext.CreateAsync();
        var item = await context.CreateIntervalAsync();
        await context.Model.InitializeAsync();
        var chart = new RecordingReviewSciChartCanvas { DataContext = context.Model };
        var window = HostEventChart(chart);
        try
        {
            EventField<NumericAxis>(chart, "xAxis").VisibleRange = new DoubleRange(0, 20);
            var origin = EventPoint(chart, item.StartSample / 500d, 0, 20);
            var target = EventPoint(chart, 4, 0, 20);
            Assert.False(chart.BeginEventMarkerDrag(item with { Source = EventSource.ImportedAnnotation }, origin));
            Assert.True(chart.BeginEventMarkerDrag(item, origin));
            Assert.True(chart.MoveEventMarkerDrag(target));
            await chart.EndEventMarkerDragAsync(new Point(-10, -10));
            Assert.False(context.Model.IsEventPlacementActive);
            Assert.Equal(item, Assert.Single(context.Model.RecordingEvents));
            Assert.True(chart.BeginEventMarkerDrag(item, origin));
            Assert.True(chart.MoveEventMarkerDrag(target));
            EventField<Grid>(chart, "chartHost").RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = Mouse.LostMouseCaptureEvent });
            Assert.False(context.Model.IsEventPlacementActive);
            await chart.EndEventMarkerDragAsync(target);
            Assert.Equal(item, Assert.Single(context.Model.RecordingEvents));
            context.Model.BeginEventPlacement(false);
            chart.DataContext = null;
            Assert.False(context.Model.IsEventPlacementActive);
            Assert.Equal(item, Assert.Single(context.Model.RecordingEvents));
        }
        finally { window.Close(); }
    });

    private static Task RunEventUiAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WpfTestApplication.Run(async () =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }
    private static T EventField<T>(RecordingReviewSciChartCanvas chart, string name) =>
        (T)typeof(RecordingReviewSciChartCanvas).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart)!;
    private static Point EventPoint(RecordingReviewSciChartCanvas chart, double seconds, double start, double end)
    {
        var plot = (Rect)typeof(RecordingReviewSciChartCanvas).GetMethod("EventPlotArea", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(chart, null)!;
        Assert.True(plot.Width > 100 && plot.Height > 100);
        return new Point(plot.Left + (seconds - start) / (end - start) * plot.Width, plot.Top + plot.Height / 2);
    }
    private static Window HostEventChart(RecordingReviewSciChartCanvas chart)
    {
        var window = new Window { Width = 1000, Height = 620, Content = chart, ShowInTaskbar = false,
            ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        PumpAnnotationUi();
        window.UpdateLayout();
        return window;
    }
}

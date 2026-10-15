using System.Reflection;
using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using BrainPlatform.Desktop.Shared.Waveform;
using SciChart.Charting.Visuals;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData(640)]
    [InlineData(1280)]
    public void Canvas_cursor_uses_plot_bounds_and_preserves_geometry_when_time_labels_switch(double width)
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var configuration = Configuration();
            var profile = Profile("采集导联", configuration);
            var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
                new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
            var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog);
            var canvas = new RecordingReviewSciChartCanvas();
            try
            {
                vm.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;
                vm.TimebaseSecondsPerScreen = 10;
                vm.InitializeAsync().GetAwaiter().GetResult();
                canvas.DataContext = vm;
                canvas.Measure(new Size(width, 400));
                canvas.Arrange(new Rect(0, 0, width, 400));
                canvas.UpdateLayout();
                var type = typeof(RecordingReviewSciChartCanvas);
                var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
                var cursor = (ReviewPlaybackCursor)type.GetField("playbackCursor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
                foreach (var position in new[] { 0d, 50, 100, 50, 0 })
                {
                    vm.SeekAsync(position).GetAwaiter().GetResult();
                    foreach (var clock in new[] { false, true })
                    {
                        if (clock) vm.TimeDisplay.IsClockTime = true;
                        else vm.TimeDisplay.IsRecordTime = true;
                        canvas.UpdateLayout();
                        Assert.True(Assert.IsAssignableFrom<FrameworkElement>(surface.GridLinesPanel).ActualWidth > 0);
                        var plot = WaveformPlotLayout.GetPlotArea(surface);
                        var expected = new Rect(surface.TranslatePoint(plot.TopLeft, cursor), plot.Size);
                        Assert.Equal(expected, typeof(ReviewPlaybackCursor).GetField("plotArea", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor));
                        Assert.Equal(position == 0 ? 0d : position == 100 ? 1d : 0.5,
                            (double)typeof(ReviewPlaybackCursor).GetField("fraction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!);
                        Assert.Equal(Visibility.Visible, cursor.Visibility);
                    }
                }
                vm.SeekAsync(50).GetAwaiter().GetResult();
                canvas.UpdateLayout();
                var held = (DoubleRange)surface.XAxes[0].VisibleRange;
                var requested = held.Max + 2;
                var setPosition = typeof(RecordingReviewViewModel).GetMethod("SetPreviewPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
                setPosition.Invoke(vm, [requested]);
                var targetRange = (DoubleRange)surface.XAxes[0].VisibleRange;
                Assert.Equal(requested - vm.VisibleDurationSeconds * 0.5, targetRange.Min, 6);
                Assert.Equal(requested + vm.VisibleDurationSeconds * 0.5, targetRange.Max, 6);
                Assert.Equal(Visibility.Visible, cursor.Visibility);
                Assert.Equal(0.5d,
                    (double)typeof(ReviewPlaybackCursor).GetField("fraction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!, 6);
                setPosition.Invoke(vm, [held.Max - 1]);
                Assert.Equal(Visibility.Visible, cursor.Visibility);
                var returnedRange = (DoubleRange)surface.XAxes[0].VisibleRange;
                Assert.Equal(held.Max - 1 - vm.VisibleDurationSeconds * 0.5, returnedRange.Min, 6);
                Assert.Equal(held.Max - 1 + vm.VisibleDurationSeconds * 0.5, returnedRange.Max, 6);
                Assert.Equal(0.5d,
                    (double)typeof(ReviewPlaybackCursor).GetField("fraction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!, 6);
            }
            finally
            {
                canvas.DataContext = null;
                vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public async Task Drag_follow_moves_left_to_center_to_right_and_is_reversible()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog);
        await vm.InitializeAsync();
        var cases = new (double Position, double Start, double Fraction)[]
        {
            (0, 0, 0), (2, 0, 0.2), (5, 0, 0.5), (50, 45, 0.5),
            (95, 90, 0.5), (98, 90, 0.8), (100, 90, 1),
        };
        foreach (var item in cases.Concat(cases.Reverse()))
        {
            vm.PreviewSeek(item.Position);
            Assert.Equal(item.Position, vm.PositionSeconds);
            Assert.Equal(item.Start, vm.ViewportStartSeconds);
            Assert.Equal(item.Fraction, (vm.PositionSeconds - vm.ViewportStartSeconds) / vm.VisibleDurationSeconds, 6);
            await vm.SeekAsync(item.Position);
            Assert.Equal(item.Start, vm.ViewportStartSeconds);
        }
    }

    [Fact]
    public async Task Short_recording_keeps_viewport_at_zero_and_allows_both_endpoints()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 3), catalog);
        await vm.InitializeAsync();
        foreach (var position in new[] { 0d, 1.5, 3, 0 })
        {
            await vm.SeekAsync(position);
            Assert.Equal(position, vm.PositionSeconds);
            Assert.Equal(0, vm.ViewportStartSeconds);
            Assert.Equal(3, vm.VisibleDurationSeconds);
        }
    }

    [Fact]
    public async Task Playback_and_track_click_use_the_same_centered_follow_policy()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var now = 0d;
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.NavigateFromTrackClickAsync(50);
        Assert.Equal(50, vm.PositionSeconds);
        Assert.Equal(45, vm.ViewportStartSeconds);
        vm.TogglePlayback();
        now = 1;
        vm.TickPlayback();
        Assert.Equal(51, vm.PositionSeconds);
        Assert.Equal(46, vm.ViewportStartSeconds);
    }
}

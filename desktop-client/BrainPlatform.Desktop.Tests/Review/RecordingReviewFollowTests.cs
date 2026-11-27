using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;
using BrainPlatform.Desktop.Shared.Waveform;
using BrainPlatform.Desktop.Tests.Shared;
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
        var lastCommittedStart = 0d;
        foreach (var item in cases.Concat(cases.Reverse()))
        {
            vm.PreviewSeek(item.Position);
            Assert.Equal(item.Position, vm.PositionSeconds);
            // Cold drag previews move only the cursor. The axis remains on the
            // last complete frame until the replacement frame is committed.
            Assert.True(
                Math.Abs(vm.ViewportStartSeconds - lastCommittedStart) < 0.000_001 ||
                Math.Abs(vm.ViewportStartSeconds - item.Start) < 0.000_001);
            await vm.SeekAsync(item.Position);
            Assert.Equal(item.Start, vm.ViewportStartSeconds);
            lastCommittedStart = item.Start;
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
    public async Task Playback_keeps_playhead_centered_while_viewport_follows()
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
        Assert.Equal(0.5, (vm.PositionSeconds - vm.ViewportStartSeconds) / vm.VisibleDurationSeconds, 6);

        now = 3;
        vm.TickPlayback();
        Assert.Equal(53, vm.PositionSeconds);
        Assert.Equal(48, vm.ViewportStartSeconds);
        Assert.Equal(0.5, (vm.PositionSeconds - vm.ViewportStartSeconds) / vm.VisibleDurationSeconds, 6);
    }

    [Fact]
    public void Canvas_keeps_waveform_scrolling_and_cursor_centered_after_dock_reparenting() =>
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var configuration = Configuration();
            var profile = Profile("采集导联", configuration);
            var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
                new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
            var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog);
            vm.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;
            vm.TimebaseSecondsPerScreen = 10;
            vm.InitializeAsync().GetAwaiter().GetResult();

            var canvas = new RecordingReviewSciChartCanvas { DataContext = vm };
            var right = new ObservableCollection<object>();
            var bottom = new ObservableCollection<object>();
            var floating = new ObservableCollection<object>();
            var host = new MultiAnalysisDockHost
            {
                Waveform = canvas,
                RightSlots = right,
                BottomSlots = bottom,
                FloatingSlots = floating,
                PanelTemplate = new DataTemplate(),
            };
            var window = new Window
            {
                Content = host,
                Width = 1280,
                Height = 720,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                window.Show();
                PumpDispatcher();
                vm.SeekAsync(30).GetAwaiter().GetResult();
                PumpDispatcher();

                var type = typeof(RecordingReviewSciChartCanvas);
                var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
                var cursor = (ReviewPlaybackCursor)type.GetField("playbackCursor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;

                // Open an algorithm panel and switch into custom dock tree (reparents waveformChrome).
                right.Add(new { AlgorithmId = "rbp", DisplayName = "RBP" });
                right.Add(new { AlgorithmId = "psd", DisplayName = "PSD" });
                PumpDispatcher();
                host.DockPaneAt("psd", DockTreeOperations.WaveformPaneId, DockDropZone.Bottom);
                PumpDispatcher();

                vm.SeekAsync(55).GetAwaiter().GetResult();
                PumpDispatcher();
                var rangeAfterCustomDock = (DoubleRange)surface.XAxes[0].VisibleRange;
                Assert.Equal(50, rangeAfterCustomDock.Min, 6);
                Assert.Equal(60, rangeAfterCustomDock.Max, 6);
                Assert.Equal(0.5d, (double)typeof(ReviewPlaybackCursor).GetField("fraction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!, 6);

                // Reset back to auto-grid layout (reparents back into workspace) and advance again.
                host.ResetToAutoLayout();
                PumpDispatcher();
                vm.SeekAsync(70).GetAwaiter().GetResult();
                PumpDispatcher();
                var rangeAfterReset = (DoubleRange)surface.XAxes[0].VisibleRange;
                Assert.Equal(65, rangeAfterReset.Min, 6);
                Assert.Equal(75, rangeAfterReset.Max, 6);
                Assert.Equal(0.5d, (double)typeof(ReviewPlaybackCursor).GetField("fraction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!, 6);
            }
            finally
            {
                window.Close();
                canvas.DataContext = null;
                vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}

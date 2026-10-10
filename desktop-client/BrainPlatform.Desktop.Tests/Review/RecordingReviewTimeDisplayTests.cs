using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SciChart.Charting.Visuals.Axes;
using SciChart.Data.Model;
using SciChart.Charting.Visuals;
using System.Windows;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData(14)]
    [InlineData(387)]
    [InlineData(999)]
    public void Canvas_time_switch_keeps_recording_aligned_grid_for_fractional_origin(int milliseconds)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var configuration = Configuration();
                var profile = Profile("采集导联", configuration);
                var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
                    new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
                var start = new DateTimeOffset(2026, 10, 10, 2, 47, 21, milliseconds, TimeSpan.Zero);
                var reader = new FakeReader(Manifest(configuration) with { RecordingStartUtc = start });
                var vm = new RecordingReviewViewModel(reader, catalog);
                var canvas = new RecordingReviewSciChartCanvas { DataContext = vm };
                try
                {
                    var axis = (NumericAxis)typeof(RecordingReviewSciChartCanvas)
                        .GetField("xAxis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
                    var ticks = Assert.IsType<WallClockSecondTickProvider>(axis.TickProvider);
                    var range = new DoubleRange(0.25, 3.25);
                    axis.VisibleRange = range;
                    for (var index = 0; index < 4; index++)
                    {
                        vm.TimeDisplay.IsClockTime = true;
                        Assert.Equal(new[] { 1d, 2d, 3d },
                            WallClockSecondTickProvider.CalculateAlignedSeconds(0.25, 3.25, ticks.OriginUtc!.Value));
                        Assert.Equal($"{start.AddSeconds(1).ToLocalTime():HH:mm:ss.fff}", axis.LabelProvider.FormatLabel(1d));
                        Assert.Same(range, axis.VisibleRange);
                        vm.TimeDisplay.IsRecordTime = true;
                        Assert.Equal(new[] { 1d, 2d, 3d },
                            WallClockSecondTickProvider.CalculateAlignedSeconds(0.25, 3.25, ticks.OriginUtc!.Value));
                        Assert.Equal("1", axis.LabelProvider.FormatLabel(1d));
                        Assert.Same(range, axis.VisibleRange);
                    }
                    Assert.Equal(0, reader.ReadCount);
                    Assert.Equal(0, vm.PositionSeconds);
                    VerifyTimeSwitchPlotLayout(canvas, vm);
                }
                finally
                {
                    canvas.DataContext = null;
                    vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyTimeSwitchPlotLayout(RecordingReviewSciChartCanvas canvas, RecordingReviewViewModel vm)
    {
        // Isolate chart layout from viewport resizing/data loads.
        canvas.DataContext = null;
        var surface = (SciChartSurface)typeof(RecordingReviewSciChartCanvas)
            .GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
        var update = typeof(RecordingReviewSciChartCanvas)
            .GetMethod("UpdateTimePresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var width in new[] { 640d, 1280d })
        {
            vm.TimeDisplay.IsRecordTime = true;
            update.Invoke(canvas, [vm]);
            canvas.Measure(new Size(width, 400));
            canvas.Arrange(new Rect(0, 0, width, 400));
            canvas.UpdateLayout();
            Assert.True(Assert.IsAssignableFrom<FrameworkElement>(surface.GridLinesPanel).ActualWidth > 0);
            var plot = WaveformPlotLayout.GetPlotArea(surface);
            vm.TimeDisplay.IsClockTime = true;
            update.Invoke(canvas, [vm]);
            canvas.Measure(new Size(width, 400));
            canvas.Arrange(new Rect(0, 0, width, 400));
            canvas.UpdateLayout();
            Assert.Equal(plot, WaveformPlotLayout.GetPlotArea(surface));
        }
    }

    [Fact]
    public async Task Switching_time_preserves_playback_frame_and_reader_work()
    {
        var configuration = Configuration();
        var profile = Profile("采集导联", configuration);
        var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
            new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
        var reader = new FakeReader(Manifest(configuration));
        var settings = new RecordingTimeDisplaySettings();
        await using var vm = new RecordingReviewViewModel(reader, catalog, timeDisplaySettings: settings);
        await vm.InitializeAsync();
        await vm.SeekAsync(17);
        vm.TogglePlayback();
        var frame = vm.CurrentFrame;
        var viewport = vm.ViewportStartSeconds;
        var reads = reader.ReadCount;
        var status = vm.PsdPreparationText;

        vm.TimeDisplay.IsClockTime = true;

        Assert.True(vm.IsPlaying);
        Assert.Equal(17, vm.PositionSeconds);
        Assert.Equal(viewport, vm.ViewportStartSeconds);
        Assert.Same(frame, vm.CurrentFrame);
        Assert.Equal(reads, reader.ReadCount);
        Assert.Equal(status, vm.PsdPreparationText);
        Assert.Equal(vm.TimeDisplay.FormatCursor(17), vm.PositionTimeText);
        Assert.Equal(RecordingTimeDisplayMode.Clock, settings.Mode);

        vm.TimeDisplay.IsRecordTime = true;
        Assert.Equal(17, vm.PositionSeconds);
        Assert.Same(frame, vm.CurrentFrame);
        Assert.Equal(reads, reader.ReadCount);
        Assert.Equal("0 s", vm.RecordingStartText);
        Assert.Contains("17 s", vm.PositionTimeText);
    }
}

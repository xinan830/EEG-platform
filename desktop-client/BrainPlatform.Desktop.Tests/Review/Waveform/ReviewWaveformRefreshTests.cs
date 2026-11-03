using System.Reflection;
using System.Windows;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewWaveformRefreshTests
{
    [Fact]
    public void Starting_playback_restores_the_complete_frame_after_viewport_display_data()
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            using var reader = new ControlledReviewReader();
            var now = 0d;
            var viewModel = new RecordingReviewViewModel(
                reader, ControlledReviewReader.Catalog, playbackClockSeconds: () => now);
            var canvas = new RecordingReviewSciChartCanvas();
            try
            {
                viewModel.InitializeAsync().GetAwaiter().GetResult();
                canvas.Measure(new Size(1280, 600));
                canvas.Arrange(new Rect(0, 0, 1280, 600));
                canvas.DataContext = viewModel;
                canvas.UpdateLayout();

                var frame = viewModel.CurrentFrame!;
                var displayTraces = frame.OutputChannelNames
                    .Select(_ => new ReviewDisplayTracePoints([0d, 10d], [1e-6, 1e-6]))
                    .ToArray();
                var displayFrame = new ReviewDisplayFrame(frame.RecordingSessionId, frame.Revision,
                    0, 10, frame.OutputChannelNames, displayTraces, frame.OutputChannelColors, frame.SourceRevision);
                var type = typeof(RecordingReviewSciChartCanvas);
                type.GetMethod("RenderDisplayFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(canvas, [displayFrame, viewModel]);
                var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(canvas)!;
                var series = Assert.IsAssignableFrom<XyDataSeries<double, double>>(surface.RenderableSeries[0].DataSeries);
                Assert.Equal(10d, series.XValues.Last());

                viewModel.TogglePlayback();
                canvas.UpdateLayout();
                var traceChanges = 0;
                series.DataSeriesChanged += (_, _) => traceChanges++;
                for (var index = 0; index < 20; index++)
                {
                    now += 0.01;
                    viewModel.TickPlayback();
                    canvas.UpdateLayout();
                }

                Assert.True(viewModel.IsPlaying);
                Assert.True(series.XValues.Last() > 10d);
                Assert.Equal(0, traceChanges);
            }
            finally
            {
                canvas.DataContext = null;
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Reapplying_the_same_frame_and_geometry_does_not_rebuild_series()
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var canvas = new RecordingReviewSciChartCanvas();
            canvas.Measure(new Size(1280, 600));
            canvas.Arrange(new Rect(0, 0, 1280, 600));
            canvas.UpdateLayout();
            var values = Enumerable.Repeat(1e-6, 20_000).ToArray();
            var frame = new RecordingReviewFrame(Guid.NewGuid(), 1, 1000, 0, 0, 20, "REF",
                [new ProjectedMontageSegment(0, values.Length, [new ProjectedMontageChannel("F3-REF", values)])], ["F3-REF"], "fixture-source");
            var type = typeof(RecordingReviewSciChartCanvas);
            var apply = type.GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(canvas, [frame, null]);
            canvas.UpdateLayout();
            apply.Invoke(canvas, [frame, null]);
            var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            var data = (XyDataSeries<double, double>)surface.RenderableSeries[0].DataSeries;
            var changes = 0;
            data.DataSeriesChanged += (_, _) => changes++;

            for (var index = 0; index < 30; index++) apply.Invoke(canvas, [frame, null]);
            Assert.Equal(0, changes);

            apply.Invoke(canvas, [frame with { Revision = 2 }, null]);
            Assert.True(changes > 0);
        });
    }

    [Fact]
    public void Reapplying_a_frame_with_only_a_different_montage_label_does_not_rebuild_series()
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var canvas = new RecordingReviewSciChartCanvas();
            canvas.Measure(new Size(1280, 600));
            canvas.Arrange(new Rect(0, 0, 1280, 600));
            canvas.UpdateLayout();
            var values = Enumerable.Repeat(1e-6, 20_000).ToArray();
            var frame = new RecordingReviewFrame(Guid.NewGuid(), 1, 1000, 0, 0, 20, "REF",
                [new ProjectedMontageSegment(0, values.Length, [new ProjectedMontageChannel("F3-REF", values)])],
                ["F3-REF"], "fixture-source");
            var type = typeof(RecordingReviewSciChartCanvas);
            var apply = type.GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(canvas, [frame, null]);
            canvas.UpdateLayout();
            var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            var data = (XyDataSeries<double, double>)surface.RenderableSeries[0].DataSeries;
            var changes = 0;
            data.DataSeriesChanged += (_, _) => changes++;

            apply.Invoke(canvas, [frame with { ViewingMontageName = "equivalent montage label" }, null]);

            Assert.Equal(0, changes);
        });
    }
}

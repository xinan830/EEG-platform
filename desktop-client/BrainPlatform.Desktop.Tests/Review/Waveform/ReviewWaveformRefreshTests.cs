using System.Reflection;
using System.Windows;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewWaveformRefreshTests
{
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
            var data = (XyDataSeries<double, double>)Assert.Single(surface.RenderableSeries).DataSeries;
            var changes = 0;
            data.DataSeriesChanged += (_, _) => changes++;

            for (var index = 0; index < 30; index++) apply.Invoke(canvas, [frame, null]);
            Assert.Equal(0, changes);

            apply.Invoke(canvas, [frame with { Revision = 2 }, null]);
            Assert.True(changes > 0);
        });
    }
}

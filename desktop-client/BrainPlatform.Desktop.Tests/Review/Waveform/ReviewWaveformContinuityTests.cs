using System.Reflection;
using System.Windows;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewWaveformContinuityTests
{
    [Fact]
    public void Contiguous_source_chunks_have_finite_sorted_time_and_no_false_break()
    {
        WithSeries(Frame(Segment(0, [1e-6, 2e-6]), Segment(2, [3e-6, 4e-6])), data =>
        {
            Assert.All(data.XValues, value => Assert.True(double.IsFinite(value)));
            Assert.Equal(new[] { 0d, .001, .002, .003 }, data.XValues.ToArray());
            Assert.All(data.YValues, value => Assert.True(double.IsFinite(value)));
            Assert.True(data.IsSorted);
        });
    }

    [Fact]
    public void Recorded_gap_breaks_y_without_invalidating_time()
    {
        WithSeries(Frame(Segment(0, [1e-6, 2e-6]), Segment(5, [3e-6, 4e-6])), data =>
        {
            Assert.All(data.XValues, value => Assert.True(double.IsFinite(value)));
            Assert.Single(data.YValues, double.IsNaN);
            Assert.True(data.IsSorted);
            Assert.True(data.XValues.Zip(data.XValues.Skip(1)).All(pair => pair.First <= pair.Second));
        });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_source_values_preserve_a_break_inside_a_decimation_bucket(double invalid)
    {
        var values = Enumerable.Repeat(1e-6, 4000).ToArray();
        values[1] = invalid;
        WithSeries(Frame(Segment(0, values)), data =>
        {
            Assert.All(data.XValues, value => Assert.True(double.IsFinite(value)));
            Assert.Contains(double.NaN, data.YValues);
            Assert.True(data.IsSorted);
        });
    }

    [Fact]
    public void Extrema_keep_temporal_order_units_and_the_first_and_last_sample()
    {
        var values = Enumerable.Repeat(2e-6, 10_000).ToArray();
        values[1] = -5e-6;
        values[3] = 9e-6;
        var frame = Frame(Segment(0, values));
        var points = ReviewWaveformTraceProjection.Build(frame, 0, 14.5, .01, 2000);

        Assert.Equal(new[] { 0d, .001, .003, .004 }, points.X.Take(4));
        Assert.Equal(new[] { 14.52, 14.45, 14.59, 14.52 }, points.Y.Take(4));
        Assert.Equal(9.999, points.X[^1]);
        Assert.InRange(points.X.Count, 2000, 8000);
        Assert.Equal(-5e-6, values[1]);
        Assert.Equal(9e-6, values[3]);
    }

    [Fact]
    public void Full_resolution_trace_uses_sample_coordinates_for_point_line_alignment()
    {
        var samples = new[] { 1e-6, 4e-6, -2e-6, 3e-6 };
        var frame = Frame(Segment(0, samples));
        var baseline = 0.5;
        var scale = 0.01;
        var trace = ReviewWaveformTraceProjection.Build(frame, 0, baseline, scale, int.MaxValue);
        var markers = ReviewSampleMarkerProjection.Build(frame, 0);

        Assert.Equal(markers.XSeconds, trace.X);
        Assert.Equal(markers.ValuesVolts.Select(value => baseline + value * 1_000_000d * scale), trace.Y);
    }

    [Fact]
    public void Nonzero_sample_origin_keeps_recording_second_coordinates()
    {
        var frame = Frame(Segment(125_000, [1e-6, 2e-6]), Segment(125_002, [3e-6])) with
        {
            WindowStartSampleCounter = 125_000,
            WindowStartSeconds = 30,
            WindowEndSeconds = 30.003,
        };
        var points = ReviewWaveformTraceProjection.Build(frame, 0, .5, 1, 2000);

        Assert.Equal(new[] { 30d, 30.001, 30.002 }, points.X);
        Assert.All(points.Y, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void Reusing_cached_values_after_a_different_frame_is_deterministic()
    {
        var values = new[] { 1e-6, 2e-6, 3e-6, 4e-6 };
        var original = Frame(Segment(0, values), Segment(4, values));
        var initial = ReviewWaveformTraceProjection.Build(original, 0, .5, 1, 2000);
        _ = ReviewWaveformTraceProjection.Build(Frame(Segment(0, [2e-6, 3e-6])), 0, .5, 1, 2000);
        var restored = ReviewWaveformTraceProjection.Build(original, 0, .5, 1, 2000);

        Assert.Equal(initial.X, restored.X);
        Assert.Equal(initial.Y, restored.Y);
        Assert.Equal(new[] { 1e-6, 2e-6, 3e-6, 4e-6 }, values);
    }

    private static RecordingReviewFrame Frame(params ProjectedMontageSegment[] segments) => new(
        Guid.NewGuid(), 1, 1000, 0, 0, 20, "REF", segments, ["F8-REF"], "fixture-source");

    private static ProjectedMontageSegment Segment(long start, double[] values) =>
        new(start, values.Length, [new ProjectedMontageChannel("F8-REF", values)]);

    private static void WithSeries(RecordingReviewFrame frame, Action<XyDataSeries<double, double>> assert)
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var canvas = new RecordingReviewSciChartCanvas();
            canvas.Measure(new Size(1280, 600));
            canvas.Arrange(new Rect(0, 0, 1280, 600));
            canvas.UpdateLayout();
            var type = typeof(RecordingReviewSciChartCanvas);
            type.GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, [frame, null]);
            var surface = (SciChartSurface)type.GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            var line = Assert.IsType<FastLineRenderableSeries>(surface.RenderableSeries[0]);
            assert((XyDataSeries<double, double>)line.DataSeries);
        });
    }
}

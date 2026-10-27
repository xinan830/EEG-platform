using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Playback;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewSampleMarkerProjectionTests
{
    [Theory]
    [InlineData(1000, 10, 50000, true)]
    [InlineData(1000, 10, 49999, false)]
    [InlineData(500, 10, 25000, true)]
    [InlineData(500, 10, 1000, false)]
    public void Marker_visibility_uses_five_dip_spacing(
        int samplingRateHz, double durationSeconds, double widthDip, bool expected) =>
        Assert.Equal(expected, ReviewSampleMarkerProjection.ShouldShowMarkers(
            samplingRateHz, durationSeconds, widthDip));

    [Fact]
    public void Projection_uses_actual_frame_samples_and_keeps_gaps_unmarked()
    {
        var frame = new RecordingReviewFrame(
            Guid.NewGuid(), 1, 1000, 125_000, 30, 30.006, "REF",
            [
                Segment(125_000, [1e-6, double.NaN, 3e-6]),
                Segment(125_004, [4e-6, 5e-6]),
            ], ["F3-REF"]);

        var markers = ReviewSampleMarkerProjection.Build(frame, 0);

        Assert.Equal(new[] { 30d, 30.002, 30.004, 30.005 }, markers.XSeconds);
        Assert.Equal(new[] { 1e-6, 3e-6, 4e-6, 5e-6 }, markers.ValuesVolts);
    }

    [Fact]
    public void Projection_does_not_use_line_extrema_as_marker_source()
    {
        var source = new[] { 0d, 10e-6, 0d, -8e-6, 0d };
        var frame = new RecordingReviewFrame(Guid.NewGuid(), 1, 1000, 0, 0, .005, "REF",
            [Segment(0, source)], ["F3-REF"]);

        var markers = ReviewSampleMarkerProjection.Build(frame, 0);

        Assert.Equal(5, markers.XSeconds.Count);
        Assert.Equal(source, markers.ValuesVolts);
    }

    private static ProjectedMontageSegment Segment(long firstCounter, double[] values) =>
        new(firstCounter, values.Length, [new ProjectedMontageChannel("F3-REF", values)]);
}


namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class SampleCounterContinuityTrackerTests
{
    [Fact]
    public void Observe_ReportsGapWithoutInventingSamples()
    {
        var tracker = new SampleCounterContinuityTracker();

        tracker.Observe(Batch(10, 2));
        var result = tracker.Observe(Batch(15, 2));

        Assert.NotNull(result.Gap);
        Assert.Equal(12, result.Gap!.FirstMissingSampleCounter);
        Assert.Equal(14, result.Gap.LastMissingSampleCounter);
        Assert.Equal(3, result.Gap.MissingSampleCount);
        Assert.Equal(17, result.NextExpectedSampleCounter);
    }

    [Fact]
    public void Observe_RejectsCounterResetOrOutOfOrderBatch()
    {
        var tracker = new SampleCounterContinuityTracker();
        tracker.Observe(Batch(10, 2));

        Assert.Throws<AcquisitionContinuityException>(() => tracker.Observe(Batch(11, 2)));
    }

    [Fact]
    public void EventCoordinateResolver_PreservesNonZeroOriginAndDoesNotCompressGaps()
    {
        var resolved = RecordingEventCoordinateResolver.Resolve(1_250, 1_000, []);
        var unavailable = RecordingEventCoordinateResolver.Resolve(
            1_120,
            1_000,
            [new AcquisitionGap(1_100, 1_149, 50, DateTimeOffset.UtcNow)]);

        Assert.Equal(250, resolved.RecordingRelativeSample);
        Assert.Equal(1_250, resolved.SourceSampleCounter);
        Assert.Equal(EventCoordinateStatus.Resolved, resolved.Status);
        Assert.Equal(120, unavailable.RecordingRelativeSample);
        Assert.Equal(1_120, unavailable.SourceSampleCounter);
        Assert.Equal(EventCoordinateStatus.UnavailableGap, unavailable.Status);
        Assert.Equal("sample_counter_gap", unavailable.UnavailableReason);
    }

    [Fact]
    public void EventCoordinateResolver_MarksCounterRollbackUnavailable()
    {
        var coordinate = RecordingEventCoordinateResolver.Resolve(999, 1_000, []);

        Assert.Equal(EventCoordinateStatus.UnavailableDiscontinuity, coordinate.Status);
        Assert.False(coordinate.IsDisplayable);
        Assert.Equal("sample_counter_precedes_recording_origin", coordinate.UnavailableReason);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount) =>
        new(firstSampleCounter, sampleCount, 1, new double[sampleCount], DateTimeOffset.UtcNow);
}

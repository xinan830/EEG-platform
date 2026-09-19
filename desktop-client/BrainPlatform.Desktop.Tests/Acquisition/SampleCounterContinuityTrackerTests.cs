using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Runtime;

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

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount) =>
        new(firstSampleCounter, sampleCount, 1, new double[sampleCount], DateTimeOffset.UtcNow);
}

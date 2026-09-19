using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Runtime;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class SampleBatchRingBufferTests
{
    [Fact]
    public void Append_EvictsOnlyDisplayHistoryWhenCapacityIsExceeded()
    {
        var buffer = new SampleBatchRingBuffer(4);

        buffer.Append(Batch(0, 2));
        var result = buffer.Append(Batch(2, 3));

        Assert.Equal(2, result.EvictedSampleCount);
        Assert.Equal(3, result.RetainedSampleCount);
        Assert.False(result.InputTooLarge);
        Assert.Single(buffer.Snapshot());
        Assert.Equal(2, buffer.Snapshot()[0].FirstSampleCounter);
    }

    [Fact]
    public void Append_RejectsOversizedDisplayBatchWithoutSplittingIt()
    {
        var buffer = new SampleBatchRingBuffer(4);
        var result = buffer.Append(Batch(0, 5));

        Assert.True(result.InputTooLarge);
        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void Snapshot_ReusesTheImmutableSnapshotUntilTheBufferChanges()
    {
        var buffer = new SampleBatchRingBuffer(4);
        buffer.Append(Batch(0, 2));

        var first = buffer.Snapshot();
        var unchanged = buffer.Snapshot();
        buffer.Append(Batch(2, 1));
        var changed = buffer.Snapshot();

        Assert.Same(first, unchanged);
        Assert.NotSame(first, changed);
        Assert.Equal(2, changed.Count);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount) =>
        new(firstSampleCounter, sampleCount, 1, new double[sampleCount], DateTimeOffset.UtcNow);
}

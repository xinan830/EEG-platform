
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
        Assert.Equal(4, buffer.LastSampleCounter);
    }

    [Fact]
    public void Append_RejectsOversizedDisplayBatchWithoutSplittingIt()
    {
        var buffer = new SampleBatchRingBuffer(4);
        var result = buffer.Append(Batch(0, 5));

        Assert.True(result.InputTooLarge);
        Assert.Empty(buffer.Snapshot());
        Assert.Null(buffer.LastSampleCounter);
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
        var compacted = Assert.Single(changed);
        Assert.Equal(3, compacted.SampleCount);
        Assert.Equal(2, first[0].SampleCount);
        Assert.Equal(2, buffer.LastSampleCounter);
    }

    [Fact]
    public void Append_CompactsOnlyContiguousDisplayBatchesAndPreservesEveryValue()
    {
        var buffer = new SampleBatchRingBuffer(16);
        buffer.Append(new AcquisitionBatch(0, 1, 2, [1d, 10d], DateTimeOffset.UtcNow));
        buffer.Append(new AcquisitionBatch(1, 1, 2, [2d, 11d], DateTimeOffset.UtcNow));
        buffer.Append(new AcquisitionBatch(3, 1, 2, [3d, 12d], DateTimeOffset.UtcNow));

        var snapshot = buffer.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, snapshot[0].SampleCount);
        Assert.Equal([1d, 10d, 2d, 11d], snapshot[0].SampleMajorValues);
        Assert.Equal(3, snapshot[1].FirstSampleCounter);
        Assert.Equal([3d, 12d], snapshot[1].SampleMajorValues);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount) =>
        new(firstSampleCounter, sampleCount, 1, new double[sampleCount], DateTimeOffset.UtcNow);
}

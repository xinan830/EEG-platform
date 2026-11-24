
namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveFilterBatchSlicerTests
{
    [Fact]
    public void Slice_PreservesSampleMajorValuesAndCountersAtTheBoundary()
    {
        var source = new AcquisitionBatch(
            firstSampleCounter: 50,
            sampleCount: 4,
            channelCount: 2,
            sampleMajorValues: [50d, 500d, 51d, 510d, 52d, 520d, 53d, 530d],
            receivedAtUtc: DateTimeOffset.UtcNow);

        var beforeBoundary = LiveFilterBatchSlicer.Slice(source, 0, 2);
        var fromBoundary = LiveFilterBatchSlicer.Slice(source, 2, 2);

        Assert.Equal(50, beforeBoundary.FirstSampleCounter);
        Assert.Equal([50d, 500d, 51d, 510d], beforeBoundary.SampleMajorValues);
        Assert.Equal(52, fromBoundary.FirstSampleCounter);
        Assert.Equal([52d, 520d, 53d, 530d], fromBoundary.SampleMajorValues);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    public void Slice_RejectsRangesOutsideTheSourceBatch(int startSampleIndex, int sampleCount)
    {
        var source = new AcquisitionBatch(0, 4, 1, [1d, 2d, 3d, 4d], DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LiveFilterBatchSlicer.Slice(source, startSampleIndex, sampleCount));
    }
}

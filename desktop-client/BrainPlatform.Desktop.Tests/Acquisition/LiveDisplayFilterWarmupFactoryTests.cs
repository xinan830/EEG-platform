using BrainPlatform.Desktop.Acquisition.Analysis;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveDisplayFilterWarmupFactoryTests
{
    [Fact]
    public void Create_UsesOnlyTheMostRecentContiguousRawSamples()
    {
        var first = Batch(100, 3, 2);
        var second = Batch(103, 3, 2);
        var third = Batch(200, 3, 2);

        var result = LiveDisplayFilterWarmupFactory.Create([first, second, third], 5);

        Assert.NotNull(result);
        Assert.Equal(200, result.FirstSampleCounter);
        Assert.Equal(3, result.SampleCount);
        Assert.Equal(third.SampleMajorValues, result.SampleMajorValues);
    }

    [Fact]
    public void Create_TrimsTheOldestPartOfAContinuousTail()
    {
        var first = Batch(100, 3, 2);
        var second = Batch(103, 3, 2);

        var result = LiveDisplayFilterWarmupFactory.Create([first, second], 4);

        Assert.NotNull(result);
        Assert.Equal(102, result.FirstSampleCounter);
        Assert.Equal(4, result.SampleCount);
        Assert.Equal([102d, 1d, 103d, 1d, 104d, 1d, 105d, 1d], result.SampleMajorValues);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter, int sampleCount, int channelCount) => new(
        firstSampleCounter,
        sampleCount,
        channelCount,
        Enumerable.Range(0, sampleCount)
            .SelectMany(index => new[] { (double)(firstSampleCounter + index), 1d })
            .ToArray(),
        DateTimeOffset.UtcNow);
}


namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class FixedDurationAnalysisBlockAggregatorTests
{
    [Fact]
    public void Append_CombinesFourKilohertzVendorBatchesIntoFiftyMillisecondBlocks()
    {
        var aggregator = new FixedDurationAnalysisBlockAggregator(50);
        var outputs = new List<AcquisitionAnalysisBatch>();

        foreach (var firstCounter in new[] { 0L, 50L, 100L, 150L })
        {
            outputs.AddRange(aggregator.Append(Batch(firstCounter, 50, 3)));
        }

        var output = Assert.Single(outputs);
        Assert.Equal(0, output.Batch.FirstSampleCounter);
        Assert.Equal(200, output.Batch.SampleCount);
        Assert.Equal(600, output.Batch.SampleMajorValues.Length);
        Assert.Equal(0d, output.Batch.SampleMajorValues[0]);
        Assert.Equal(599d, output.Batch.SampleMajorValues[^1]);
    }

    [Fact]
    public void Append_NeverCombinesAcrossAnExplicitAcquisitionGap()
    {
        var aggregator = new FixedDurationAnalysisBlockAggregator(50);
        var initial = Batch(0, 100, 2);
        Assert.Empty(aggregator.Append(initial));

        var gap = new AcquisitionGap(100, 119, 20, DateTimeOffset.UtcNow);
        var afterGap = Batch(120, 100, 2) with { GapBeforeBatch = gap };
        var flushed = aggregator.Append(afterGap).ToArray();

        var first = Assert.Single(flushed);
        Assert.Equal(0, first.Batch.FirstSampleCounter);
        Assert.Equal(100, first.Batch.SampleCount);
        var trailing = Assert.IsType<AcquisitionAnalysisBatch>(aggregator.Flush());
        Assert.Equal(120, trailing.Batch.FirstSampleCounter);
        Assert.Equal(100, trailing.Batch.SampleCount);
        Assert.Equal(gap, trailing.GapBeforeBatch);
    }

    private static AcquisitionAnalysisBatch Batch(long firstSampleCounter, int sampleCount, int channelCount)
    {
        var values = Enumerable.Range(0, sampleCount * channelCount)
            .Select(value => (double)(firstSampleCounter * channelCount + value))
            .ToArray();
        return new AcquisitionAnalysisBatch(
            Guid.Parse("8c0b2e63-fb81-440c-9a4b-94e565aee387"),
            new AcquisitionStreamMetadata(
                "device",
                "device",
                4_000,
                Enumerable.Range(0, channelCount)
                    .Select(index => new AcquisitionChannel(index, index, $"CH {index}", AcquisitionChannelKind.Reference, "V"))
                    .ToArray(),
                channelCount - 1,
                DateTimeOffset.UtcNow),
            new AcquisitionBatch(firstSampleCounter, sampleCount, channelCount, values, DateTimeOffset.UtcNow),
            null,
            "recording");
    }
}

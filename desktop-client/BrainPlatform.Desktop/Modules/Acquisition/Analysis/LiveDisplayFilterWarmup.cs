using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Analysis;

/// <summary>Contiguous raw context used only to initialize the next display filter.</summary>
public sealed record LiveDisplayFilterWarmup(
    long FirstSampleCounter,
    int SampleCount,
    int ChannelCount,
    double[] SampleMajorValues,
    DateTimeOffset ReceivedAtUtc)
{
    public long LastSampleCounter => checked(FirstSampleCounter + SampleCount - 1L);
}

public static class LiveDisplayFilterWarmupFactory
{
    public static LiveDisplayFilterWarmup? Create(
        IReadOnlyList<AcquisitionBatch> rawBatches,
        int maximumSamples)
    {
        if (rawBatches.Count == 0 || maximumSamples <= 0)
        {
            return null;
        }

        var selected = new List<AcquisitionBatch>();
        var selectedSamples = 0;
        long? expectedFirstCounter = null;
        var channelCount = rawBatches[^1].ChannelCount;

        for (var index = rawBatches.Count - 1; index >= 0 && selectedSamples < maximumSamples; index--)
        {
            var batch = rawBatches[index];
            if (batch.ChannelCount != channelCount ||
                (expectedFirstCounter is not null && batch.LastSampleCounter + 1 != expectedFirstCounter))
            {
                break;
            }

            selected.Add(batch);
            selectedSamples += batch.SampleCount;
            expectedFirstCounter = batch.FirstSampleCounter;
        }

        if (selected.Count == 0)
        {
            return null;
        }

        selected.Reverse();
        var discardLeadingSamples = Math.Max(0, selectedSamples - maximumSamples);
        var retainedSamples = selectedSamples - discardLeadingSamples;
        var values = new double[checked(retainedSamples * channelCount)];
        var targetOffset = 0;
        long? firstCounter = null;

        foreach (var batch in selected)
        {
            var sourceStartSample = Math.Min(discardLeadingSamples, batch.SampleCount);
            discardLeadingSamples -= sourceStartSample;
            var copiedSamples = batch.SampleCount - sourceStartSample;
            if (copiedSamples == 0)
            {
                continue;
            }

            firstCounter ??= batch.FirstSampleCounter + sourceStartSample;
            Array.Copy(
                batch.SampleMajorValues,
                sourceStartSample * channelCount,
                values,
                targetOffset,
                copiedSamples * channelCount);
            targetOffset += copiedSamples * channelCount;
        }

        return new LiveDisplayFilterWarmup(
            firstCounter ?? throw new InvalidOperationException("Warm-up contains no samples."),
            retainedSamples,
            channelCount,
            values,
            selected[^1].ReceivedAtUtc);
    }
}

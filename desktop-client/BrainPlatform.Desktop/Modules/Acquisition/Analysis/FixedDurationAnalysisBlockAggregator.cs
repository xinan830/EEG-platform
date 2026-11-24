
namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>
/// Decouples vendor read granularity from the Python display-filter boundary.
/// Raw recording still receives every vendor batch immediately; only optional
/// live analysis is combined into contiguous fixed-duration blocks.
/// </summary>
internal sealed class FixedDurationAnalysisBlockAggregator
{
    private readonly int blockDurationMilliseconds;
    private readonly List<BatchSlice> slices = [];
    private AcquisitionAnalysisBatch? template;
    private int pendingSamples;

    public FixedDurationAnalysisBlockAggregator(int blockDurationMilliseconds = 50)
    {
        if (blockDurationMilliseconds <= 0 || blockDurationMilliseconds > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(blockDurationMilliseconds));
        }

        this.blockDurationMilliseconds = blockDurationMilliseconds;
    }

    public IEnumerable<AcquisitionAnalysisBatch> Append(AcquisitionAnalysisBatch incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var targetSamples = GetTargetSampleCount(incoming.Stream.SamplingRateHz);

        if (template is not null && !CanAppendToCurrentBlock(incoming))
        {
            var flushed = Flush();
            if (flushed is not null)
            {
                yield return flushed;
            }
        }

        var sourceOffset = 0;
        while (sourceOffset < incoming.Batch.SampleCount)
        {
            if (template is null)
            {
                template = incoming with { Batch = incoming.Batch, GapBeforeBatch = incoming.GapBeforeBatch };
            }

            var remainingCapacity = targetSamples - pendingSamples;
            var copiedSamples = Math.Min(remainingCapacity, incoming.Batch.SampleCount - sourceOffset);
            slices.Add(new BatchSlice(incoming.Batch, sourceOffset, copiedSamples));
            pendingSamples += copiedSamples;
            sourceOffset += copiedSamples;

            if (pendingSamples == targetSamples)
            {
                var completed = Flush();
                if (completed is not null)
                {
                    yield return completed;
                }
            }
        }
    }

    public AcquisitionAnalysisBatch? Flush()
    {
        if (template is null || pendingSamples == 0)
        {
            return null;
        }

        var firstSlice = slices[0];
        var channelCount = firstSlice.Batch.ChannelCount;
        var values = new double[checked(pendingSamples * channelCount)];
        var destinationOffset = 0;
        var receivedAtUtc = firstSlice.Batch.ReceivedAtUtc;
        foreach (var slice in slices)
        {
            Array.Copy(
                slice.Batch.SampleMajorValues,
                checked(slice.StartSampleIndex * channelCount),
                values,
                destinationOffset,
                checked(slice.SampleCount * channelCount));
            destinationOffset += checked(slice.SampleCount * channelCount);
            receivedAtUtc = slice.Batch.ReceivedAtUtc;
        }

        var output = template with
        {
            Batch = new AcquisitionBatch(
                firstSlice.Batch.FirstSampleCounter + firstSlice.StartSampleIndex,
                pendingSamples,
                channelCount,
                values,
                receivedAtUtc),
        };
        slices.Clear();
        template = null;
        pendingSamples = 0;
        return output;
    }

    private bool CanAppendToCurrentBlock(AcquisitionAnalysisBatch incoming)
    {
        var current = template!;
        if (incoming.GapBeforeBatch is not null || incoming.SessionId != current.SessionId ||
            incoming.Stream.SamplingRateHz != current.Stream.SamplingRateHz ||
            incoming.LiveMontage?.Revision != current.LiveMontage?.Revision ||
            incoming.Batch.ChannelCount != current.Batch.ChannelCount ||
            !string.Equals(incoming.RawRecordingDirectory, current.RawRecordingDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        var previous = slices[^1];
        var expectedFirstSampleCounter = previous.Batch.FirstSampleCounter + previous.StartSampleIndex + previous.SampleCount;
        return incoming.Batch.FirstSampleCounter == expectedFirstSampleCounter;
    }

    private int GetTargetSampleCount(int samplingRateHz) => checked(Math.Max(
        1,
        (int)Math.Round(samplingRateHz * blockDurationMilliseconds / 1_000d, MidpointRounding.AwayFromZero)));

    internal static bool CanMergeContiguous(AcquisitionAnalysisBatch previous, AcquisitionAnalysisBatch next)
    {
        if (next.GapBeforeBatch is not null ||
            next.SessionId != previous.SessionId ||
            next.Stream.SamplingRateHz != previous.Stream.SamplingRateHz ||
            next.LiveMontage?.Revision != previous.LiveMontage?.Revision ||
            next.Batch.ChannelCount != previous.Batch.ChannelCount ||
            !string.Equals(next.RawRecordingDirectory, previous.RawRecordingDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        return next.Batch.FirstSampleCounter == previous.Batch.FirstSampleCounter + previous.Batch.SampleCount;
    }

    internal static AcquisitionAnalysisBatch MergeContiguous(IReadOnlyList<AcquisitionAnalysisBatch> blocks)
    {
        if (blocks.Count == 1) return blocks[0];
        var first = blocks[0];
        var last = blocks[^1];
        var channelCount = first.Batch.ChannelCount;
        var totalSamples = 0;
        for (var i = 0; i < blocks.Count; i++) totalSamples = checked(totalSamples + blocks[i].Batch.SampleCount);

        var values = new double[checked(totalSamples * channelCount)];
        var offset = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var src = blocks[i].Batch.SampleMajorValues;
            Array.Copy(src, 0, values, offset, src.Length);
            offset += src.Length;
        }

        return first with
        {
            Batch = new AcquisitionBatch(
                first.Batch.FirstSampleCounter,
                totalSamples,
                channelCount,
                values,
                last.Batch.ReceivedAtUtc),
        };
    }

    private sealed record BatchSlice(AcquisitionBatch Batch, int StartSampleIndex, int SampleCount);
}

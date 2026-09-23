
namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

public sealed record RingBufferAppendResult(int RetainedSampleCount, int EvictedSampleCount, bool InputTooLarge);

/// <summary>
/// Bounded in-memory history for future rendering only. Raw persistence happens
/// before this buffer, so eviction cannot discard a recording.
/// </summary>
public sealed class SampleBatchRingBuffer
{
    private const int TargetBlockSampleCount = 256;
    private readonly object gate = new();
    private readonly Queue<RetainedDisplayBlock> blocks = new();
    private readonly int capacitySamples;
    private readonly int blockSampleCapacity;
    private int retainedSamples;
    private AcquisitionBatch[] cachedSnapshot = [];
    private bool snapshotIsCurrent = true;
    private RetainedDisplayBlock? activeBlock;
    private long cachedLastSampleCounter;

    public SampleBatchRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        }

        this.capacitySamples = capacitySamples;
        blockSampleCapacity = Math.Min(TargetBlockSampleCount, capacitySamples);
    }

    public int RetainedSampleCount
    {
        get
        {
            lock (gate)
            {
                return retainedSamples;
            }
        }
    }

    public long? LastSampleCounter
    {
        get
        {
            lock (gate)
            {
                return retainedSamples > 0 ? cachedLastSampleCounter : null;
            }
        }
    }

    public RingBufferAppendResult Append(AcquisitionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            if (batch.SampleCount > capacitySamples)
            {
                var evicted = retainedSamples;
                blocks.Clear();
                activeBlock = null;
                retainedSamples = 0;
                cachedLastSampleCounter = 0;
                snapshotIsCurrent = false;
                return new RingBufferAppendResult(retainedSamples, evicted, true);
            }

            var evictedSamples = 0;
            while (retainedSamples + batch.SampleCount > capacitySamples && blocks.TryDequeue(out var oldest))
            {
                retainedSamples -= oldest.SampleCount;
                evictedSamples += oldest.SampleCount;
                if (ReferenceEquals(activeBlock, oldest))
                {
                    activeBlock = null;
                }
            }

            var sourceSampleIndex = 0;
            while (sourceSampleIndex < batch.SampleCount)
            {
                if (activeBlock is null || !activeBlock.CanAppend(batch, sourceSampleIndex))
                {
                    activeBlock = new RetainedDisplayBlock(
                        checked(batch.FirstSampleCounter + sourceSampleIndex),
                        batch.ChannelCount,
                        blockSampleCapacity);
                    blocks.Enqueue(activeBlock);
                }

                sourceSampleIndex += activeBlock.Append(batch, sourceSampleIndex);
            }

            retainedSamples += batch.SampleCount;
            cachedLastSampleCounter = batch.LastSampleCounter;
            snapshotIsCurrent = false;
            return new RingBufferAppendResult(retainedSamples, evictedSamples, false);
        }
    }

    public IReadOnlyList<AcquisitionBatch> Snapshot()
    {
        lock (gate)
        {
            if (!snapshotIsCurrent)
            {
                cachedSnapshot = blocks.Select(block => block.ToBatch()).ToArray();
                snapshotIsCurrent = true;
            }

            return cachedSnapshot;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            blocks.Clear();
            activeBlock = null;
            retainedSamples = 0;
            cachedLastSampleCounter = 0;
            cachedSnapshot = [];
            snapshotIsCurrent = true;
        }
    }

    private sealed class RetainedDisplayBlock
    {
        private readonly long firstSampleCounter;
        private readonly int channelCount;
        private readonly int sampleCapacity;
        private readonly double[] values;
        private AcquisitionBatch? cachedBatch;
        private DateTimeOffset receivedAtUtc;

        public RetainedDisplayBlock(long firstSampleCounter, int channelCount, int sampleCapacity)
        {
            this.firstSampleCounter = firstSampleCounter;
            this.channelCount = channelCount;
            this.sampleCapacity = sampleCapacity;
            values = new double[checked(channelCount * sampleCapacity)];
        }

        public int SampleCount { get; private set; }

        public bool CanAppend(AcquisitionBatch batch, int sourceSampleIndex) =>
            SampleCount < sampleCapacity &&
            batch.ChannelCount == channelCount &&
            checked(batch.FirstSampleCounter + sourceSampleIndex) == checked(firstSampleCounter + SampleCount);

        public int Append(AcquisitionBatch batch, int sourceSampleIndex)
        {
            var copiedSamples = Math.Min(sampleCapacity - SampleCount, batch.SampleCount - sourceSampleIndex);
            Array.Copy(
                batch.SampleMajorValues,
                checked(sourceSampleIndex * channelCount),
                values,
                checked(SampleCount * channelCount),
                checked(copiedSamples * channelCount));
            SampleCount += copiedSamples;
            receivedAtUtc = batch.ReceivedAtUtc;
            cachedBatch = null;
            return copiedSamples;
        }

        public AcquisitionBatch ToBatch()
        {
            if (cachedBatch is not null)
            {
                return cachedBatch;
            }

            double[] batchValues;
            if (SampleCount == sampleCapacity)
            {
                batchValues = values;
            }
            else
            {
                batchValues = new double[checked(SampleCount * channelCount)];
                Array.Copy(values, batchValues, batchValues.Length);
            }

            cachedBatch = new AcquisitionBatch(
                firstSampleCounter,
                SampleCount,
                channelCount,
                batchValues,
                receivedAtUtc);
            return cachedBatch;
        }
    }
}

using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Runtime;

public sealed record RingBufferAppendResult(int RetainedSampleCount, int EvictedSampleCount, bool InputTooLarge);

/// <summary>
/// Bounded in-memory history for future rendering only. Raw persistence happens
/// before this buffer, so eviction cannot discard a recording.
/// </summary>
public sealed class SampleBatchRingBuffer
{
    private readonly object gate = new();
    private readonly Queue<AcquisitionBatch> batches = new();
    private readonly int capacitySamples;
    private int retainedSamples;
    private AcquisitionBatch[] cachedSnapshot = [];
    private bool snapshotIsCurrent = true;

    public SampleBatchRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        }

        this.capacitySamples = capacitySamples;
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

    public RingBufferAppendResult Append(AcquisitionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            if (batch.SampleCount > capacitySamples)
            {
                var evicted = retainedSamples;
                batches.Clear();
                retainedSamples = 0;
                snapshotIsCurrent = false;
                return new RingBufferAppendResult(retainedSamples, evicted, true);
            }

            var evictedSamples = 0;
            while (retainedSamples + batch.SampleCount > capacitySamples && batches.TryDequeue(out var oldest))
            {
                retainedSamples -= oldest.SampleCount;
                evictedSamples += oldest.SampleCount;
            }

            batches.Enqueue(batch);
            retainedSamples += batch.SampleCount;
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
                cachedSnapshot = batches.ToArray();
                snapshotIsCurrent = true;
            }

            return cachedSnapshot;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            batches.Clear();
            retainedSamples = 0;
            cachedSnapshot = [];
            snapshotIsCurrent = true;
        }
    }
}

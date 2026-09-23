using System.Threading.Channels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>
/// Owns the short-lived queue and cancellation state while a replacement
/// Python filter session warms up and catches the active display stream.
/// </summary>
internal sealed class PendingLiveFilterTransition
{
    private readonly Channel<AcquisitionBatch> batches = Channel.CreateUnbounded<AcquisitionBatch>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly object taskGate = new();
    private Task? worker;
    private long processedThroughRawSampleCounter = -1;
    private long firstQueuedRawSampleCounter = -1;
    private string? filterSessionId;
    private Exception? failure;

    public PendingLiveFilterTransition(
        LiveDisplayFilterSettings settings,
        long revision,
        long requestedFromRawSampleCounter,
        IReadOnlyList<AcquisitionBatch> warmupBatches,
        int maximumWarmupSamples)
    {
        Settings = settings;
        Revision = revision;
        RequestedFromRawSampleCounter = requestedFromRawSampleCounter;
        WarmupBatches = warmupBatches;
        MaximumWarmupSamples = maximumWarmupSamples;
    }

    public LiveDisplayFilterSettings Settings { get; }
    public long Revision { get; }
    public long RequestedFromRawSampleCounter { get; }
    public IReadOnlyList<AcquisitionBatch> WarmupBatches { get; }
    public int MaximumWarmupSamples { get; }
    public int WarmupSampleCount { get; private set; }
    public string? FilterSessionId => Volatile.Read(ref filterSessionId);
    public Exception? Failure => Volatile.Read(ref failure);
    public CancellationToken CancellationToken => cancellation.Token;
    public long ProcessedThroughRawSampleCounter => Volatile.Read(ref processedThroughRawSampleCounter);

    public void StartIfRequired(Func<Task> action, CancellationToken acquisitionCancellation)
    {
        lock (taskGate)
        {
            if (worker is not null)
            {
                return;
            }

            if (acquisitionCancellation.IsCancellationRequested)
            {
                Cancel();
                return;
            }

            worker = Task.Run(action, cancellation.Token);
        }
    }

    public void Enqueue(AcquisitionBatch batch)
    {
        Interlocked.CompareExchange(ref firstQueuedRawSampleCounter, batch.FirstSampleCounter, -1);
        if (!batches.Writer.TryWrite(batch))
        {
            throw new InvalidOperationException("实时滤波切换缓冲区已关闭。");
        }
    }

    public IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync() =>
        batches.Reader.ReadAllAsync(cancellation.Token);

    public void MarkSessionCreated(string sessionId) => Volatile.Write(ref filterSessionId, sessionId);
    public LiveDisplayFilterWarmup? CreateWarmup()
    {
        var warmup = LiveDisplayFilterWarmupFactory.Create(WarmupBatches, MaximumWarmupSamples);
        var firstQueued = Volatile.Read(ref firstQueuedRawSampleCounter);
        if (warmup is not null && firstQueued >= 0 && warmup.LastSampleCounter + 1 != firstQueued)
        {
            // Never carry causal state over a raw/dispatch gap.
            warmup = null;
        }
        WarmupSampleCount = warmup?.SampleCount ?? 0;
        return warmup;
    }
    public void MarkProcessed(long sampleCounter) => Volatile.Write(ref processedThroughRawSampleCounter, sampleCounter);
    public void MarkFailed(Exception exception) => Volatile.Write(ref failure, exception);
    public void Complete() => batches.Writer.TryComplete();

    public void Cancel()
    {
        cancellation.Cancel();
        batches.Writer.TryComplete();
    }

    public async Task ObserveCompletionAsync()
    {
        Task? current;
        lock (taskGate)
        {
            current = worker;
        }
        if (current is null)
        {
            return;
        }

        try
        {
            await current;
        }
        catch (OperationCanceledException)
        {
            // Expected when a pending transition is superseded or stopped.
        }
    }
}

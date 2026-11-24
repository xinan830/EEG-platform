using System.Threading.Channels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Transfers live EEG blocks without blocking device capture.</summary>
public sealed class BoundedLiveAccessDispatcher : IAsyncDisposable
{
    private const int LiveAccessBlockDurationMilliseconds = 50;
    private readonly Channel<AcquisitionAnalysisBatch> channel;
    private readonly ILiveSampleAccessBridge bridge;
    private readonly FixedDurationAnalysisBlockAggregator aggregator =
        new(LiveAccessBlockDurationMilliseconds);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private AcquisitionFault? fault;

    public BoundedLiveAccessDispatcher(ILiveSampleAccessBridge bridge, int capacity = 8)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        channel = Channel.CreateBounded<AcquisitionAnalysisBatch>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        worker = Task.Run(ConsumeAsync);
    }

    public event EventHandler<AcquisitionFault>? Faulted;

    public AcquisitionFault? Fault => Volatile.Read(ref fault);

    public bool TryQueue(AcquisitionAnalysisBatch batch)
    {
        if (Fault is not null) return false;
        foreach (var block in aggregator.Append(batch))
        {
            if (TryEnqueue(block)) continue;
            return false;
        }

        return true;
    }

    private bool TryEnqueue(AcquisitionAnalysisBatch batch)
    {
        if (channel.Writer.TryWrite(batch)) return true;
        ReportFault("LIVE_ACCESS_QUEUE_OVERFLOW", "实时样本访问传输落后于设备采集，读取边界已报告溢出。");
        return false;
    }

    public async Task CloseSessionAsync(CancellationToken cancellationToken)
    {
        if (Fault is null && aggregator.Flush() is { } trailingBlock)
        {
            TryEnqueue(trailingBlock);
        }

        channel.Writer.TryComplete();
        await worker.WaitAsync(cancellationToken);
        await bridge.CloseSessionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        cancellation.Cancel();
        try { await worker; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(cancellation.Token))
            {
                await bridge.PublishAsync(batch, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportFault("LIVE_ACCESS_BRIDGE_FAILED", exception.Message);
        }
    }

    private void ReportFault(string code, string detail)
    {
        var failure = new AcquisitionFault(code, detail, DateTimeOffset.UtcNow);
        if (Interlocked.CompareExchange(ref fault, failure, null) is not null) return;
        channel.Writer.TryComplete();
        Faulted?.Invoke(this, failure);
    }
}

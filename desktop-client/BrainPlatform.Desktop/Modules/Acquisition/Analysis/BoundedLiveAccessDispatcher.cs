using System.Threading.Channels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Analysis;

/// <summary>Transfers live EEG blocks without blocking device capture.</summary>
public sealed class BoundedLiveAccessDispatcher : IAsyncDisposable
{
    private readonly Channel<AcquisitionAnalysisBatch> channel;
    private readonly ILiveSampleAccessBridge bridge;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private int isFaulted;

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

    public bool TryQueue(AcquisitionAnalysisBatch batch)
    {
        if (Volatile.Read(ref isFaulted) != 0) return false;
        if (channel.Writer.TryWrite(batch)) return true;
        if (Interlocked.Exchange(ref isFaulted, 1) == 0)
        {
            Faulted?.Invoke(this, new AcquisitionFault(
                "LIVE_ACCESS_QUEUE_OVERFLOW",
                "实时样本访问传输落后于设备采集，读取边界已报告溢出。",
                DateTimeOffset.UtcNow));
            channel.Writer.TryComplete();
        }
        return false;
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
            await bridge.DisposeAsync();
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
            if (Interlocked.Exchange(ref isFaulted, 1) == 0)
            {
                Faulted?.Invoke(this, new AcquisitionFault(
                    "LIVE_ACCESS_BRIDGE_FAILED", exception.Message, DateTimeOffset.UtcNow));
            }
        }
    }
}

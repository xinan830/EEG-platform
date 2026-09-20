using System.Threading.Channels;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Analysis;

public sealed record AnalysisDispatchResult(
    bool Accepted,
    string? RejectionReason,
    bool IsNewFailure = false);

/// <summary>
/// Separates capture from optional analysis transport. A slow or unavailable
/// Python consumer can lose analysis notifications, but never blocks raw
/// recording or the device read loop.
/// </summary>
public sealed class BoundedAnalysisDispatcher : IAsyncDisposable
{
    private const int DisplayFilterBlockDurationMilliseconds = 50;
    private readonly Channel<AcquisitionAnalysisBatch> channel;
    private readonly FixedDurationAnalysisBlockAggregator aggregator =
        new(DisplayFilterBlockDurationMilliseconds);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private int isFaulted;

    public BoundedAnalysisDispatcher(IAcquisitionAnalysisBridge bridge, int capacity = 8)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        channel = Channel.CreateBounded<AcquisitionAnalysisBatch>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        worker = Task.Run(() => ConsumeAsync(bridge, cancellation.Token));
    }

    public event EventHandler<AcquisitionFault>? AnalysisFaulted;

    public AnalysisDispatchResult TryQueue(AcquisitionAnalysisBatch batch)
    {
        if (Volatile.Read(ref isFaulted) != 0)
        {
            return new AnalysisDispatchResult(false, "Analysis pipeline is unavailable.");
        }

        foreach (var block in aggregator.Append(batch))
        {
            if (!channel.Writer.TryWrite(block))
            {
                return FaultQueue("Analysis queue is full or closed; live filtering fell behind the device stream.");
            }
        }

        return new AnalysisDispatchResult(true, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref isFaulted) == 0 && aggregator.Flush() is { } trailingBlock)
        {
            channel.Writer.TryWrite(trailingBlock);
        }
        channel.Writer.TryComplete();
        cancellation.Cancel();
        try
        {
            await worker;
        }
        catch (OperationCanceledException)
        {
            // Expected during desktop shutdown.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ConsumeAsync(IAcquisitionAnalysisBridge bridge, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var block in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await PublishBlockAsync(bridge, block, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            FaultQueue($"Analysis worker stopped unexpectedly: {exception.Message}");
        }
    }

    private async Task PublishBlockAsync(
        IAcquisitionAnalysisBridge bridge,
        AcquisitionAnalysisBatch block,
        CancellationToken cancellationToken)
    {
        try
        {
            await bridge.PublishAsync(block, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            FaultQueue(exception.Message, "ANALYSIS_BRIDGE_FAILED");
        }
    }

    private AnalysisDispatchResult FaultQueue(
        string detail,
        string code = "ANALYSIS_QUEUE_OVERFLOW")
    {
        var isNewFailure = Interlocked.Exchange(ref isFaulted, 1) == 0;
        if (isNewFailure)
        {
            channel.Writer.TryComplete();
            AnalysisFaulted?.Invoke(
                this,
                new AcquisitionFault(code, detail, DateTimeOffset.UtcNow));
        }

        return new AnalysisDispatchResult(false, detail, isNewFailure);
    }
}

using System.Threading.Channels;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Acquisition.Analysis;

public sealed record AnalysisDispatchResult(bool Accepted, string? RejectionReason);

/// <summary>
/// Separates capture from optional analysis transport. A slow or unavailable
/// Python consumer can lose analysis notifications, but never blocks raw
/// recording or the device read loop.
/// </summary>
public sealed class BoundedAnalysisDispatcher : IAsyncDisposable
{
    private const int DisplayFilterBlockDurationMilliseconds = 50;
    private readonly Channel<AcquisitionAnalysisBatch> channel;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;

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
        if (channel.Writer.TryWrite(batch))
        {
            return new AnalysisDispatchResult(true, null);
        }

        return new AnalysisDispatchResult(false, "Analysis queue is full or closed.");
    }

    public async ValueTask DisposeAsync()
    {
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
        var aggregator = new FixedDurationAnalysisBlockAggregator(DisplayFilterBlockDurationMilliseconds);
        await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken))
        {
            foreach (var block in aggregator.Append(batch))
            {
                await PublishBlockAsync(bridge, block, cancellationToken);
            }
        }

        if (aggregator.Flush() is { } trailingBlock)
        {
            await PublishBlockAsync(bridge, trailingBlock, cancellationToken);
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
            AnalysisFaulted?.Invoke(
                this,
                new AcquisitionFault(
                    "ANALYSIS_BRIDGE_FAILED",
                    exception.Message,
                    DateTimeOffset.UtcNow));
        }
    }
}

using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class BoundedLiveAccessDispatcherTests
{
    [Fact]
    public async Task QueueOverflowIsReportedWithoutBlockingTheCaptureCaller()
    {
        var bridge = new BlockingBridge();
        await using var dispatcher = new BoundedLiveAccessDispatcher(bridge, capacity: 1);
        dispatcher.Faulted += (_, fault) => bridge.FaultDetail = fault.Detail;
        AcquisitionAnalysisBatch batch = CreateBatch(0);
        Assert.True(dispatcher.TryQueue(batch));
        await bridge.Started.Task;

        Assert.True(dispatcher.TryQueue(CreateBatch(1)));
        Assert.False(dispatcher.TryQueue(CreateBatch(2)));
        Assert.Contains("溢出", bridge.FaultDetail ?? string.Empty);
    }

    private static AcquisitionAnalysisBatch CreateBatch(long firstSampleCounter) => new(
        Guid.NewGuid(),
        new AcquisitionStreamMetadata(
            "device", "device", 250,
            [new AcquisitionChannel(0, 0, "Fz", AcquisitionChannelKind.Reference, "V")],
            0, DateTimeOffset.UtcNow),
        new AcquisitionBatch(firstSampleCounter, 1, 1, [1d], DateTimeOffset.UtcNow),
        null, string.Empty);

    private sealed class BlockingBridge : ILiveSampleAccessBridge
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? FaultDetail { get; set; }

        public async Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

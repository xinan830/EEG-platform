
namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class BoundedAnalysisDispatcherTests
{
    [Fact]
    public async Task TinyVendorBatches_AreAggregatedBeforeTheyConsumeQueueCapacity()
    {
        var bridge = new BlockingBridge();
        await using var dispatcher = new BoundedAnalysisDispatcher(bridge, capacity: 1);

        for (var counter = 0; counter < 200; counter++)
        {
            Assert.True(dispatcher.TryQueue(Batch(counter)).Accepted);
        }

        await bridge.WaitUntilStartedAsync();

        for (var counter = 200; counter < 400; counter++)
        {
            Assert.True(dispatcher.TryQueue(Batch(counter)).Accepted);
        }

        AnalysisDispatchResult result = new(true, null);
        for (var counter = 400; counter < 600; counter++)
        {
            result = dispatcher.TryQueue(Batch(counter));
        }

        Assert.False(result.Accepted);
        Assert.True(result.IsNewFailure);
        var later = dispatcher.TryQueue(Batch(600));
        Assert.False(later.Accepted);
        Assert.False(later.IsNewFailure);
    }

    private static AcquisitionAnalysisBatch Batch(long firstSampleCounter)
    {
        var metadata = new AcquisitionStreamMetadata(
            "device",
            "device",
            4_000,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow);
        return new AcquisitionAnalysisBatch(
            Guid.Parse("7420ce84-bb31-405d-9455-98b4215b5db8"),
            metadata,
            new AcquisitionBatch(
                firstSampleCounter,
                1,
                2,
                [firstSampleCounter * 1e-6, firstSampleCounter],
                DateTimeOffset.UtcNow),
            null,
            string.Empty);
    }

    private sealed class BlockingBridge : IAcquisitionAnalysisBridge
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public async Task WaitUntilStartedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await started.Task.WaitAsync(timeout.Token);
        }
    }
}

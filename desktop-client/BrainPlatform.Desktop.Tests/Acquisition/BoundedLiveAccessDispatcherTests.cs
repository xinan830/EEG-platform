using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class BoundedLiveAccessDispatcherTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task QueueOverflowIsReportedWithoutBlockingTheCaptureCaller()
    {
        var bridge = new BlockingBridge();
        await using var dispatcher = new BoundedLiveAccessDispatcher(bridge, capacity: 1);
        dispatcher.Faulted += (_, fault) => bridge.FaultDetail = fault.Detail;
        AcquisitionAnalysisBatch batch = CreateBatch(0, sampleCount: 13);
        Assert.True(dispatcher.TryQueue(batch));
        await bridge.Started.Task;

        Assert.True(dispatcher.TryQueue(CreateBatch(13, sampleCount: 13)));
        Assert.False(dispatcher.TryQueue(CreateBatch(26, sampleCount: 13)));
        Assert.Contains("溢出", bridge.FaultDetail ?? string.Empty);
    }

    [Fact]
    public async Task TransportFailureIsReportedOnTheLiveBranch()
    {
        var bridge = new FailingBridge();
        await using var dispatcher = new BoundedLiveAccessDispatcher(bridge);
        var failure = new TaskCompletionSource<AcquisitionFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Faulted += (_, fault) => failure.TrySetResult(fault);

        Assert.True(dispatcher.TryQueue(CreateBatch(0, sampleCount: 13)));

        var fault = await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("LIVE_ACCESS_BRIDGE_FAILED", fault.Code);
    }

    [Fact]
    public async Task SessionCloseWaitsUntilQueuedSamplesHaveBeenPublished()
    {
        var bridge = new CountingBridge();
        await using var dispatcher = new BoundedLiveAccessDispatcher(bridge, capacity: 2);
        Assert.True(dispatcher.TryQueue(CreateBatch(0, sampleCount: 13)));
        Assert.True(dispatcher.TryQueue(CreateBatch(13, sampleCount: 13)));

        await dispatcher.CloseSessionAsync(CancellationToken.None);

        Assert.Equal(2, bridge.Published);
        Assert.True(bridge.Closed);
    }

    [Fact]
    public async Task DispatcherCanBeRecreatedWithoutDisposingRuntimeOwnedBridge()
    {
        var bridge = new CountingBridge();
        await using (var dispatcher = new BoundedLiveAccessDispatcher(bridge))
        {
            Assert.True(dispatcher.TryQueue(CreateBatch(0, sampleCount: 13)));
            await dispatcher.CloseSessionAsync(CancellationToken.None);
        }

        await bridge.PublishAsync(CreateBatch(13, sampleCount: 13), CancellationToken.None);
        Assert.Equal(2, bridge.Published);
        Assert.False(bridge.Disposed);
    }

    [Fact]
    public async Task CoalescesSmallContiguousBatchesAndFlushesTheTailOnClose()
    {
        var bridge = new CountingBridge();
        await using var dispatcher = new BoundedLiveAccessDispatcher(bridge);

        for (var sample = 0; sample < 15; sample++)
        {
            Assert.True(dispatcher.TryQueue(CreateBatch(sample, sampleCount: 1)));
        }

        await dispatcher.CloseSessionAsync(CancellationToken.None);

        Assert.Equal(new[] { 13, 2 }, bridge.Batches.Select(batch => batch.Batch.SampleCount));
        Assert.Equal(Enumerable.Range(0, 15).Select(value => (double)value),
            bridge.Batches.SelectMany(batch => batch.Batch.SampleMajorValues));
    }

    private static AcquisitionAnalysisBatch CreateBatch(long firstSampleCounter, int sampleCount) => new(
        SessionId,
        new AcquisitionStreamMetadata(
            "device", "device", 250,
            [new AcquisitionChannel(0, 0, "Fz", AcquisitionChannelKind.Reference, "V")],
            0, DateTimeOffset.UtcNow),
        new AcquisitionBatch(firstSampleCounter, sampleCount, 1,
            Enumerable.Range(0, sampleCount).Select(index => (double)(firstSampleCounter + index)).ToArray(), DateTimeOffset.UtcNow),
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

        public Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, System.Text.Json.JsonElement config, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CloseSessionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated { add { } remove { } }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingBridge : ILiveSampleAccessBridge
    {
        public int Published { get; private set; }
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }
        public List<AcquisitionAnalysisBatch> Batches { get; } = [];
        public Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Published++;
            Batches.Add(batch);
            return Task.CompletedTask;
        }
        public Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, System.Text.Json.JsonElement config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseSessionAsync(CancellationToken cancellationToken)
        {
            Closed = true;
            return Task.CompletedTask;
        }
        public event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated { add { } remove { } }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingBridge : ILiveSampleAccessBridge
    {
        public Task PublishAsync(AcquisitionAnalysisBatch batch, CancellationToken cancellationToken) =>
            throw new HttpRequestException("backend unavailable");
        public Task SetAlgorithmEnabledAsync(string algorithmId, bool enabled, System.Text.Json.JsonElement config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseSessionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public event EventHandler<LiveAnalysisUpdate>? AnalysisUpdated { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

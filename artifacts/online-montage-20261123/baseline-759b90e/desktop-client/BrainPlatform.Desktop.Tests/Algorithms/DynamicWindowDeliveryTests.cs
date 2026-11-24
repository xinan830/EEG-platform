using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class DynamicWindowDeliveryTests
{
    [Fact]
    public async Task Terminal_result_is_delivered_while_its_subscription_release_is_still_pending()
    {
        var client = new ReleaseClient("ready");
        var observer = new DynamicWindowObserver(client);
        using var token = new CancellationTokenSource();
        var result = await observer.ObserveAsync(Request(), token.Token).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("window", result.WindowId);
        Assert.Equal("ready", result.State);
        Assert.Equal("subscription", client.ReleasedId);
        Assert.False(client.Release.Task.IsCompleted);
        token.Cancel();
        Assert.False(client.ReleaseToken.IsCancellationRequested);
        Assert.True(client.ReleaseToken.CanBeCanceled);
        client.Release.SetResult(new DynamicSubscriptionResponse("subscription", "window", "request", null, "cancelled"));
    }

    [Fact]
    public async Task Cancelling_a_pending_window_still_awaits_its_own_bounded_subscription_release()
    {
        var client = new ReleaseClient("queued");
        var observer = new DynamicWindowObserver(client);
        using var token = new CancellationTokenSource();
        var observation = observer.ObserveAsync(Request(), token.Token);
        token.Cancel();
        await client.ReleaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(observation.IsCompleted);
        Assert.False(client.ReleaseToken.IsCancellationRequested);
        client.Release.SetResult(new DynamicSubscriptionResponse("subscription", "window", "request", null, "cancelled"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation);
    }

    [Fact]
    public async Task Terminal_cleanup_failure_is_observed_without_discarding_the_completed_result()
    {
        var client = new ReleaseClient("ready");
        client.Release.SetException(new HttpRequestException("release failed"));
        var result = await new DynamicWindowObserver(client).ObserveAsync(Request(), CancellationToken.None);
        Assert.Equal("ready", result.State);
        Assert.Equal("subscription", client.ReleasedId);
    }

    private static DynamicWindowRequest Request() => new("recording", "faa", null, null,
        JsonSerializer.SerializeToElement(new { algorithm_id = "faa" }), 20000);

    private sealed class ReleaseClient(string state) : IAlgorithmClient
    {
        public TaskCompletionSource<DynamicSubscriptionResponse> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ReleasedId { get; private set; }
        public CancellationToken ReleaseToken { get; private set; }

        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request, CancellationToken token) =>
            Task.FromResult(new DynamicWindowResponse("session", "window", request.RequestId!, state,
                null, null, null, null, null, null, "subscription"));
        public Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(string id, string? project,
            CancellationToken token)
        {
            ReleasedId = id;
            ReleaseToken = token;
            ReleaseStarted.SetResult();
            return Release.Task.WaitAsync(token);
        }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string id, int maxCells, CancellationToken token) => throw new NotSupportedException();
    }
}

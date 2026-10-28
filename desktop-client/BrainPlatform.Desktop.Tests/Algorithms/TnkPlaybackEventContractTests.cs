using System.Text.Json;
using System.Reflection;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkPlaybackEventContractTests
{
    [Fact]
    public void EventBatchDeserializesResumeAndGapMetadata()
    {
        const string json = """
            {
              "delivery_id": "delivery-1",
              "events": [],
              "next_sequence": 4,
              "latest_sequence": 5,
              "has_more": true,
              "coverage": "incomplete",
              "discontinuity": {
                "first_missing_sequence": 2,
                "last_missing_sequence": 3,
                "affected_start_sample": 1000,
                "affected_end_sample": 6000,
                "sample_range_exact": true
              },
              "delivery": { "state": "enabled" }
            }
            """;

        var batch = JsonSerializer.Deserialize<TnkPlaybackEventBatchResponse>(json);

        Assert.NotNull(batch);
        Assert.Equal("delivery-1", batch.DeliveryId);
        Assert.Equal(4, batch.NextSequence);
        Assert.Equal("incomplete", batch.Coverage);
        Assert.Equal(2, batch.Discontinuity?.FirstMissingSequence);
        Assert.Equal(6000, batch.Discontinuity?.AffectedEndSample);
    }

    [Fact]
    public async Task IdleTnkOnDemandSessionDoesNotStartPlaybackEventPolling()
    {
        var client = new IdleTnkClient();
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter());
        typeof(AlgorithmListViewModel).GetProperty("RegisteredRecording", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(catalog, new RegisteredRecording("recording", "fixture", ".wpf", "", 100, 10, ["Fz"], "source"));

        var request = new AnalysisRunRequest("recording", "dynamic", JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "tnk_biofeedback", mode = "dynamic", time = new { start_s = 0d, end_s = 10d },
            refresh_step_s = 1d,
        }));
        var begin = typeof(AlgorithmListViewModel).GetMethod("BeginWindowAnalysisAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)begin.Invoke(catalog, [request, new TnkShadowRunResultHandler("tnk_biofeedback"), 0d])!;

        Assert.False(catalog.IsTnkPlaybackEventConsumerActive);
        Assert.Equal(1, client.EnsureCalls);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    private sealed class IdleTnkClient : IAlgorithmClient
    {
        public int EnsureCalls { get; private set; }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            var identity = JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId });
            var range = new DynamicWindowRange(0, request.CursorSample, 0, request.CursorSample / 100d);
            return Task.FromResult(new DynamicWindowResponse("session", "window", request.RequestId ?? "request",
                "insufficient_data", identity, null, range, "insufficient_data", null, null));
        }
    }
}

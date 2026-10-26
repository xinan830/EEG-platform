using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewOnDemandAnalysisTests
{
    [Fact]
    public async Task Provider_maps_ready_backend_arrays_and_units_without_recomputing()
    {
        var client = new FakeClient(ReadyResponse());
        var provider = new PsdReviewAnalysisAdapter(client, enableOnDemand: true);
        var request = new AnalysisRunRequest(
            "recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));

        var window = await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None);

        Assert.Equal("available", window.Status);
        Assert.Equal(500, window.CursorSample);
        Assert.Equal([1d, 2d, 3d], window.Axes!["axis_frequency_hz"].Deserialize<double[]>()!);
        Assert.Equal([4d, 5d, 6d], window.Arrays!["psd"].Deserialize<double[]>()!);
        Assert.Equal("V^2/Hz", window.ArrayMetadata!["psd"].GetProperty("unit").GetString());
        Assert.Empty(client.Subscribed);
        Assert.Single(client.Cancelled);
    }

    [Fact]
    public async Task Snapshot_overload_rejects_backend_identity_from_another_source()
    {
        var response = ReadyResponse() with
        {
            Identity = JsonSerializer.SerializeToElement(new
            {
                recording_id = "recording", source_sha256 = "different-source", algorithm_id = "psd"
            })
        };
        var provider = new PsdReviewAnalysisAdapter(new FakeClient(response), enableOnDemand: true);
        using var config = JsonDocument.Parse("{\"algorithm_id\":\"psd\"}");
        var snapshot = new ReviewAnalysisSnapshot("recording", "expected-source", "psd", 500,
            config.RootElement.Clone(), null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.LoadOnDemandAsync("recording", snapshot, 500, CancellationToken.None, null));

        Assert.Contains("身份与不可变分析快照不一致", exception.Message);
    }

    [Fact]
    public async Task Snapshot_overload_rejects_scientific_configuration_that_differs_from_request()
    {
        const string scientificJson = "{\"config\":{\"algorithm_id\":\"psd\",\"channel\":\"F7-REF\",\"scientific_version\":\"v1\"},\"context\":{},\"scientific_configuration_contract\":\"scientific-config-v1\"}";
        var response = SnapshotResponse(scientificJson);
        var provider = new PsdReviewAnalysisAdapter(new FakeClient(response), enableOnDemand: true);
        using var config = JsonDocument.Parse("{\"algorithm_id\":\"psd\",\"channel\":\"F3-REF\",\"scientific_version\":\"v1\"}");
        var snapshot = new ReviewAnalysisSnapshot("recording", "source", "psd", 500,
            config.RootElement.Clone(), null);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.LoadOnDemandAsync("recording", snapshot, 500, CancellationToken.None, null));
        Assert.Contains("科学配置与不可变分析快照不一致", exception.Message);
    }

    [Fact]
    public async Task Snapshot_overload_rejects_scientific_configuration_digest_mismatch()
    {
        var response = SnapshotResponse("{\"config\":{\"algorithm_id\":\"psd\"},\"context\":{},\"scientific_configuration_contract\":\"scientific-config-v1\"}") with
        {
            Identity = JsonSerializer.SerializeToElement(new
            {
                recording_id = "recording", source_sha256 = "source", algorithm_id = "psd",
                configuration_sha256 = "incorrect-digest"
            })
        };
        var provider = new PsdReviewAnalysisAdapter(new FakeClient(response), enableOnDemand: true);
        using var config = JsonDocument.Parse("{\"algorithm_id\":\"psd\"}");
        var snapshot = new ReviewAnalysisSnapshot("recording", "source", "psd", 500,
            config.RootElement.Clone(), null);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.LoadOnDemandAsync("recording", snapshot, 500, CancellationToken.None, null));
        Assert.Contains("摘要校验失败", exception.Message);
    }

    private static DynamicWindowResponse SnapshotResponse(string scientificJson)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scientificJson))).ToLowerInvariant();
        return ReadyResponse() with
        {
            ScientificConfigurationJson = scientificJson,
            Identity = JsonSerializer.SerializeToElement(new
            {
                recording_id = "recording", source_sha256 = "source", algorithm_id = "psd",
                configuration_sha256 = digest
            })
        };
    }

    [Fact]
    public async Task Provider_polls_inflight_window_and_cancels_page_subscription()
    {
        var client = new FakeClient(QueuedResponse(), ReadyResponse());
        var provider = new PsdReviewAnalysisAdapter(client, enableOnDemand: true);
        var request = new AnalysisRunRequest(
            "recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));

        var window = await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None);

        Assert.Equal("available", window.Status);
        Assert.Equal(1, client.GetCount);
        Assert.Single(client.Cancelled);
    }

    [Fact]
    public async Task Provider_preserves_scientific_rejection_state()
    {
        var rejected = Response("rejected", null) with
        {
            Failure = new DynamicWindowFailure("QUALITY_GATE", "质量门未通过")
        };
        var client = new FakeClient(rejected);
        var provider = new PsdReviewAnalysisAdapter(client, enableOnDemand: true);
        var request = new AnalysisRunRequest(
            "recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));

        var window = await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None);

        Assert.Equal("rejected", window.Status);
        Assert.Contains("QUALITY_GATE", window.ReasonCodes);
    }

    [Theory]
    [InlineData("insufficient_data")]
    [InlineData("unavailable")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task Provider_preserves_each_terminal_state_and_reason(string state)
    {
        var response = Response(state, null) with { Failure = new DynamicWindowFailure("REASON", "具体原因") };
        var provider = new PsdReviewAnalysisAdapter(new FakeClient(response), enableOnDemand: true);
        var request = new AnalysisRunRequest("recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));
        var result = await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None);
        Assert.Equal(state, result.Status);
        Assert.Contains("REASON", result.ReasonCodes);
        Assert.Null(result.Arrays);
    }

    [Fact]
    public async Task Returning_to_released_target_explicitly_retries_cancelled_generation()
    {
        var cancelled = Response("cancelled", null) with
        {
            SubscriptionId = "old-subscription",
            Failure = new DynamicWindowFailure("WINDOW_NO_ACTIVE_SUBSCRIBERS", "窗口已无活动订阅者")
        };
        var client = new FakeClient(cancelled, ReadyResponse());
        var observer = new DynamicWindowObserver(client);
        var request = new DynamicWindowRequest("recording", "psd", null, null,
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd" }), 500, RequestId: "first-request");
        var ready = await observer.ObserveAsync(request, CancellationToken.None);
        Assert.Equal("ready", ready.State);
        Assert.Equal(2, client.Ensured.Count);
        Assert.False(client.Ensured[0].Retry);
        Assert.True(client.Ensured[1].Retry);
        Assert.Equal("window", client.Ensured[1].RetryOfWindowId);
        Assert.NotEqual(request.RequestId, client.Ensured[1].RequestId);
        Assert.Equal(["old-subscription", "subscription"], client.Cancelled);
    }

    [Fact]
    public async Task Scientific_version_is_not_sent_as_definition_version()
    {
        var client = new FakeClient(ReadyResponse());
        var provider = new PsdReviewAnalysisAdapter(client, enableOnDemand: true);
        var request = new AnalysisRunRequest("recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));
        await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None);
        Assert.Null(client.Ensured[0].DefinitionVersion);
        Assert.Equal("v1", client.Ensured[0].Config.GetProperty("scientific_version").GetString());
    }

    private static DynamicWindowResponse ReadyResponse() => Response(
        "ready",
        new DynamicWindowResultResponse(
            null,
            "ready",
            "spectrum",
            new Dictionary<string, JsonElement>
            {
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d }),
            },
            new Dictionary<string, string> { ["frequency_hz"] = "Hz" },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new[] { 4d, 5d, 6d }),
            },
            new Dictionary<string, string> { ["psd"] = "V^2/Hz" },
            "valid", null, null, null));

    [Fact]
    public async Task Provider_reports_actual_preparation_and_computation_progress()
    {
        var client = new FakeClient(QueuedResponse(), Response("preparing_source", null),
            Response("computing_window", null), ReadyResponse());
        var provider = new PsdReviewAnalysisAdapter(client, enableOnDemand: true);
        var statuses = new StatusCollector();
        var request = new AnalysisRunRequest("recording", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "psd", scientific_version = "v1" }));
        var result = await provider.LoadOnDemandAsync("recording", request, 500, CancellationToken.None, statuses);
        Assert.Equal("available", result.Status);
        Assert.Contains("preparing_source", statuses.States);
        Assert.Contains("computing_window", statuses.States);
        Assert.Single(client.Cancelled);
    }

    private sealed class StatusCollector : IProgress<DynamicWindowResponse>
    {
        public List<string> States { get; } = [];
        public void Report(DynamicWindowResponse value) => States.Add(value.State);
    }

    private static DynamicWindowResponse QueuedResponse() => Response("queued", null);

    private static DynamicWindowResponse Response(string state, DynamicWindowResultResponse? result) => new(
        "session", "window", "request", state,
        JsonSerializer.SerializeToElement(new { recording_id = "recording", scientific_version = "v1", implementation_identity = "test", start_sample = 0, end_sample = 500 }),
        new DynamicWindowPlanResponse(500, 0, 500, 0, 5, "planned", null),
        new DynamicWindowRange(0, 500, 0, 5),
        "valid", null, result, state == "ready" ? "subscription" : null);

    private sealed class FakeClient(params DynamicWindowResponse[] responses) : IAlgorithmClient
    {
        private int responseIndex;
        public int GetCount { get; private set; }
        public List<string> Subscribed { get; } = [];
        public List<string> Cancelled { get; } = [];
        public List<DynamicWindowRequest> Ensured { get; } = [];

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
        public Task<WindowPreviewResponse> GetWindowPreviewAsync(string runId, long? cursorSample, double? cursorSeconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request, CancellationToken cancellationToken)
        {
            Ensured.Add(request);
            return Task.FromResult(responses[Math.Min(Ensured.Count - 1, responses.Length - 1)]);
        }
        public Task<DynamicWindowResponse> GetDynamicWindowAsync(string windowId, string? projectId, CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(responses[Math.Min(++responseIndex, responses.Length - 1)]);
        }
        public Task<DynamicSubscriptionResponse> SubscribeDynamicWindowAsync(string windowId, string requestId, string? projectId, CancellationToken cancellationToken)
        {
            Subscribed.Add(windowId);
            return Task.FromResult(new DynamicSubscriptionResponse("subscription", windowId, requestId, projectId, "active"));
        }
        public Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(string subscriptionId, string? projectId, CancellationToken cancellationToken)
        {
            Cancelled.Add(subscriptionId);
            return Task.FromResult(new DynamicSubscriptionResponse(subscriptionId, "window", "request", projectId, "cancelled"));
        }
    }
}

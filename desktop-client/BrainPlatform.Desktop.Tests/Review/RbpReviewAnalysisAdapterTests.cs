using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RbpReviewAnalysisAdapterTests
{
    [Fact]
    public async Task Dynamic_request_keeps_recording_algorithm_channel_and_ratio_unit()
    {
        var client = new FixtureClient { Response = ReadyResponse() };
        var adapter = new RbpReviewAnalysisAdapter(client, enableOnDemand: true);
        var request = new AnalysisRunRequest("recording-1", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = "rbp", channel = "F7" }));

        var response = await adapter.LoadOnDemandAsync("recording-1", request, 1_000,
            CancellationToken.None);

        Assert.Equal("recording-1", client.LastRequest!.RecordingId);
        Assert.Equal("rbp", client.LastRequest.AlgorithmId);
        Assert.Equal("F7", client.LastRequest.Config.GetProperty("channel").GetString());
        Assert.Equal("ratio", response.ArrayMetadata!["alpha"].GetProperty("unit").GetString());
        Assert.Equal(900, response.Window!.StartSample);
        Assert.Equal(1_000, response.Window.EndSample);
    }

    [Fact]
    public async Task Catalog_entry_must_declare_runnable_dynamic_capability()
    {
        var client = new FixtureClient { DeclaresDynamicCapability = false };
        var adapter = new RbpReviewAnalysisAdapter(client, enableOnDemand: true);

        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.GetAlgorithmAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Review_adapter_does_not_create_full_runs_or_read_legacy_windows()
    {
        var adapter = new ReviewWindowAnalysisAdapter(new FixtureClient(), "rbp", "RBP", enableOnDemand: true);

        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.EnsureRunAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", JsonSerializer.SerializeToElement(new { algorithm_id = "rbp" })),
            null, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.LoadAsync("recording-1", "run-1", 1,
            CancellationToken.None));
    }

    [Fact]
    public async Task Current_dynamic_window_topomap_request_carries_power_type_and_band_without_changing_window()
    {
        var client = new FixtureClient();
        var adapter = new RbpReviewAnalysisAdapter(client, enableOnDemand: true);
        var window = new WindowPreviewResponse("run", "recording-1", 1_000, 5, "available", [], null,
            false, null, null, null, null, null, null, "rbp-v1", "impl-v1")
        {
            DynamicResponse = ReadyResponse(),
        };

        var result = await adapter.LoadTopomapAsync(window, CancellationToken.None, ["rbp.absolute.alpha"]);

        Assert.Same(client.Topomap, result);
        Assert.Equal("window-1", client.TopomapWindowId);
        Assert.Equal(["rbp.absolute.alpha"], client.TopomapFieldIds);
    }

    [Fact]
    public void Topomap_capability_is_owned_by_the_rbp_adapter()
    {
        var rbp = new RbpReviewAnalysisAdapter(new FixtureClient(), enableOnDemand: true);
        var stft = new ReviewWindowAnalysisAdapter(new FixtureClient { AlgorithmId = "stft" }, "stft", "STFT");

        Assert.Same(rbp, new ReviewAnalysisCoordinator(rbp).GetCapability<IReviewTopomapProvider>());
        Assert.Null(new ReviewAnalysisCoordinator(stft).GetCapability<IReviewTopomapProvider>());
    }

    private static DynamicWindowResponse ReadyResponse() => new(
        "session-1", "window-1", "request-1", "ready",
        JsonSerializer.SerializeToElement(new { recording_id = "recording-1" }),
        null,
        new DynamicWindowRange(900, 1_000, 4.5, 5),
        "clean", null,
        new DynamicWindowResultResponse(
            null, "ready", "algorithm_group", null, null,
            new Dictionary<string, JsonElement>
            {
                ["alpha"] = JsonSerializer.SerializeToElement(new[] { 0.4 }),
            },
            new Dictionary<string, string> { ["alpha"] = "ratio" },
            "clean", null, null, null),
        "subscription-1");

    [Theory]
    [InlineData("rbp")]
    [InlineData("stft")]
    public async Task Shared_adapter_routes_only_its_declared_algorithm(string algorithmId)
    {
        var client = new FixtureClient { AlgorithmId = algorithmId };
        var adapter = new ReviewWindowAnalysisAdapter(client, algorithmId, algorithmId.ToUpperInvariant(), true);
        Assert.Equal(algorithmId, (await adapter.GetAlgorithmAsync(CancellationToken.None)).Id);
        var request = new AnalysisRunRequest("recording-1", "official_algorithm",
            JsonSerializer.SerializeToElement(new { algorithm_id = algorithmId, channel = "F7" }));
        await adapter.LoadOnDemandAsync("recording-1", request, 1000, CancellationToken.None);
        Assert.Equal(algorithmId, client.LastRequest!.AlgorithmId);
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.LoadOnDemandAsync("recording-1",
            request with { Config = JsonSerializer.SerializeToElement(new { algorithm_id = "other" }) },
            1000, CancellationToken.None));
    }

    private sealed class FixtureClient : IAlgorithmClient
    {
        public string AlgorithmId { get; init; } = "rbp";
        public bool DeclaresDynamicCapability { get; init; } = true;
        public DynamicWindowResponse Response { get; init; } = ReadyResponse();
        public DynamicWindowRequest? LastRequest { get; private set; }
        public TopomapResponse Topomap { get; init; } = new("window", "recording-1", 0, 5,
            "ready", "rbp-v1", "impl-v1", "montage_position_snapshot", "mri", "m", [], null, null);
        public string? TopomapWindowId { get; private set; }
        public IReadOnlyList<string>? TopomapFieldIds { get; private set; }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlgorithmCatalogItem>>([CatalogItem()]);

        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(
            DynamicWindowRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response with { RequestId = request.RequestId ?? "request-1" });
        }

        public Task<TopomapResponse> GetDynamicTopomapAsync(string windowId, string? projectId,
            CancellationToken cancellationToken, IReadOnlyList<string> fieldIds)
        {
            TopomapWindowId = windowId;
            TopomapFieldIds = fieldIds;
            return Task.FromResult(Topomap);
        }

        public Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(
            string subscriptionId, string? projectId, CancellationToken cancellationToken) =>
            Task.FromResult(new DynamicSubscriptionResponse(subscriptionId, "window-1", "request-1", projectId, "cancelled"));

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private AlgorithmCatalogItem CatalogItem() => new(
            "official", AlgorithmId, "fixture-v1", AlgorithmId, AlgorithmId, "", [], ["dynamic"], default,
            new DynamicAnalysisPolicy(4, [5], 5, 1, true), "available", true,
            "official-rbp", "1", "rbp-runtime", DeclaresDynamicCapability
                ? JsonSerializer.SerializeToElement(new { on_demand = new { implementation = "implemented" } })
                : JsonSerializer.SerializeToElement(new { on_demand = new { implementation = "unsupported" } }));
    }
}

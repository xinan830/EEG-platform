using System.Reflection;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

internal sealed class OnDemandDetailClient : IAlgorithmClient
{
    public List<DynamicWindowRequest> Requests { get; } = [];
    public Func<DynamicWindowRequest, DynamicWindowResponse> Respond { get; set; } = RbpResponse;
    public int FullRunCalls { get; private set; }
    public int Released { get; private set; }

    internal static AlgorithmListViewModel Catalog(OnDemandDetailClient client, string id = "rbp", string unit = "ratio",
        string primaryChannel = "F3", string secondaryChannel = "F7")
    {
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter())
        {
            SelectedAlgorithm = new AlgorithmCatalogItem("official", id, "fixture-v1", id, id, "", [],
                ["static", "dynamic"], default, new DynamicAnalysisPolicy(4, [5, 10], 5, 1, true),
                "available", true, null, null, null),
        };
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.RegisteredRecording))!
            .SetValue(catalog, new RegisteredRecording("recording", "fixture", ".wpf", "2026-10-13T00:00:00Z",
                200, 30, [primaryChannel, secondaryChannel], "hash"));
        catalog.RegisteredChannels.Add(primaryChannel);
        catalog.RegisteredChannels.Add(secondaryChannel);
        catalog.SelectedChannel = primaryChannel;
        catalog.SelectedF4Channel = secondaryChannel;
        catalog.EndSecondsText = "30";
        catalog.SelectedAnalysisMode = "动态";
        return catalog;
    }

    internal static Task Run(AlgorithmListViewModel catalog) =>
        (Task)typeof(AlgorithmListViewModel).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(catalog, null)!;

    internal static DynamicWindowResponse RbpResponse(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        var state = end < 4 ? "insufficient_data" : "ready";
        string[] names = ["delta", "theta", "alpha", "beta", "gamma"];
        double[] shares = [0.1, 0.1, 0.6, 0.15, 0.05];
        var result = new DynamicWindowResultResponse(null, state, "multi_metric_series",
            new Dictionary<string, JsonElement> { ["time_s"] = JsonSerializer.SerializeToElement(new[] { end }) },
            new Dictionary<string, string> { ["time_s"] = "s" },
            names.Select((name, i) => (name, value: JsonSerializer.SerializeToElement(new[] { shares[i] })))
                .ToDictionary(pair => pair.name, pair => pair.value),
            names.ToDictionary(name => name, _ => "ratio"), "clean", null, null,
            JsonSerializer.SerializeToElement(new { analysis_state = end < 5 ? "Partial" : "Complete" }));
        return new DynamicWindowResponse("session", $"window-{request.CursorSample}", request.RequestId!, state,
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
            new DynamicWindowRange(Math.Max(0, request.CursorSample - 1000), request.CursorSample, Math.Max(0, end - 5), end),
            state == "ready" ? "clean" : "insufficient_data",
            state == "ready" ? null : new DynamicWindowFailure("RBP_WINDOW_INSUFFICIENT_DATA", "数据不足", null),
            state == "ready" ? result : null, "subscription");
    }

    public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request, CancellationToken token)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }
    public Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(string id, string? project, CancellationToken token)
    {
        Released++;
        return Task.FromResult(new DynamicSubscriptionResponse(id, "window", "request", project, "cancelled"));
    }
    public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken token)
    {
        FullRunCalls++;
        throw new NotSupportedException();
    }
    public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken token) => throw new NotSupportedException();
    public Task<AnalysisRunResponse> GetRunAsync(string id, CancellationToken token) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string id, CancellationToken token) => throw new NotSupportedException();
    public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string id, int cells, CancellationToken token) => throw new NotSupportedException();
}

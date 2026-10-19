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
    public int CancelledRuns { get; private set; }
    public int GetRunCalls { get; private set; }
    public int FullWindowPreviewCalls { get; private set; }
    public int StructuredPreviewCalls { get; private set; }
    public List<AnalysisRunRequest> FullRunRequests { get; } = [];
    public Queue<AnalysisRunResponse> PollResponses { get; } = [];
    public Func<AnalysisRunRequest, AnalysisRunResponse> RespondFullRun { get; set; } = ThrowFullRun;
    public Func<string, long, WindowPreviewResponse> RespondFullWindowPreview { get; set; } =
        (_, sample) => FullPsdWindowPreview(sample);
    public Func<string, StructuredPreviewResponse> RespondFullStructuredPreview { get; set; } =
        _ => FullPsdStructuredPreview();

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
        catalog.SelectedDynamicExecutionMode = "按需分析";
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
        FullRunRequests.Add(request);
        return Task.FromResult(RespondFullRun(request));
    }
    public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken token) => throw new NotSupportedException();
    public Task<AnalysisRunResponse> GetRunAsync(string id, CancellationToken token)
    {
        GetRunCalls++;
        if (PollResponses.TryDequeue(out var response)) return Task.FromResult(response);
        throw new NotSupportedException();
    }
    public Task<AnalysisRunResponse> CancelRunAsync(string id, CancellationToken token)
    {
        CancelledRuns++;
        return Task.FromResult(new AnalysisRunResponse(
            id, "recording", "official_algorithm", "cancelled", "fixture-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, 10,
            "analysis", "已取消"));
    }
    public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string id, CancellationToken token) => throw new NotSupportedException();
    public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string id, int cells, CancellationToken token)
    {
        StructuredPreviewCalls++;
        return Task.FromResult(RespondFullStructuredPreview(id));
    }
    public Task<WindowPreviewResponse> GetWindowPreviewAsync(
        string id, long? cursorSample, double? cursorSeconds, CancellationToken token)
    {
        FullWindowPreviewCalls++;
        var sample = cursorSample ?? checked((long)Math.Round(cursorSeconds!.Value * 200));
        return Task.FromResult(RespondFullWindowPreview(id, sample));
    }

    internal static DynamicWindowResponse PsdResponse(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        var frequencies = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d });
        var values = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 1d });
        var result = new DynamicWindowResultResponse(
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }),
            "ready", "frequency_series",
            new Dictionary<string, JsonElement> { ["frequency_hz"] = frequencies },
            new Dictionary<string, string> { ["frequency_hz"] = "Hz" },
            new Dictionary<string, JsonElement> { ["psd"] = values },
            new Dictionary<string, string> { ["psd"] = "V^2/Hz" },
            "clean", null, null, null);
        return new DynamicWindowResponse(
            "session", $"window-{request.CursorSample}", request.RequestId!, "ready",
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
            new DynamicWindowRange(Math.Max(0, request.CursorSample - 1000), request.CursorSample,
                Math.Max(0, end - 5), end), "clean", null, result, "subscription");
    }

    internal static DynamicWindowResponse StftResponse(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        if (end < 4)
            return new DynamicWindowResponse(
                "session", $"window-{request.CursorSample}", request.RequestId!, "insufficient_data",
                JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
                new DynamicWindowRange(0, request.CursorSample, 0, end), "insufficient_data",
                new DynamicWindowFailure("STFT_WINDOW_INSUFFICIENT_DATA", "数据不足"), null, "subscription");
        var result = new DynamicWindowResultResponse(
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), "ready", "time_frequency",
            new Dictionary<string, JsonElement>
            {
                ["time_center_s"] = JsonSerializer.SerializeToElement(new[] { 2d, 3d }),
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 10d, 11d }),
            },
            new Dictionary<string, string> { ["time_center_s"] = "s", ["frequency_hz"] = "Hz" },
            new Dictionary<string, JsonElement>
            {
                ["power_linear"] = JsonSerializer.SerializeToElement(new[] { new[] { 1e-12, 2e-12 }, new[] { 2e-12, 3e-12 } }),
                ["power_db"] = JsonSerializer.SerializeToElement(new[] { new[] { 1d, 2d }, new[] { 2d, 3d } }),
            },
            new Dictionary<string, string> { ["power_linear"] = "V^2/Hz", ["power_db"] = "dB re 1 uV^2/Hz" },
            "clean", null, null,
            JsonSerializer.SerializeToElement(new
            {
                preprocessing_scope = "whole_recording_before_window_slice",
                inner_time_axis = "relative_window_time_center_s",
                analysis_state = "Complete",
            }));
        return new DynamicWindowResponse(
            "session", $"window-{request.CursorSample}", request.RequestId!, "ready",
            JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }), null,
            new DynamicWindowRange(request.CursorSample - 1000, request.CursorSample, end - 5, end),
            "clean", null, result, "subscription");
    }

    internal static WindowPreviewResponse FullPsdWindowPreview(long sample) =>
        new(
            "full-run-1", "recording", sample, sample / 200d, "available", [],
            new WindowPreviewWindow("full-window-1", 0, 0, 1000, "Complete", "valid", []),
            false, JsonSerializer.SerializeToElement(new { kind = "frequency_series" }),
            JsonSerializer.SerializeToElement("clean"),
            new Dictionary<string, JsonElement>
            {
                ["axis_frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d }),
            },
            new Dictionary<string, JsonElement>
            {
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new { array_key = "axis_frequency_hz", unit = "Hz" }),
            },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new[] { 9d, 8d, 7d }),
            },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new { unit = "V^2/Hz" }),
            },
            "fixture-v1", "fixture-v1");

    internal static StructuredPreviewResponse FullPsdStructuredPreview() =>
        new(
            "full-run-1",
            new RunArtifact("artifact", "full-run-1", "official_algorithm", "artifact.npz",
                "application/npz", 1, "sha256", "V^2/Hz",
                new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
            JsonSerializer.SerializeToElement(new { kind = "frequency_series" }),
            ["F3"],
            new TimeRange(0, 5),
            new TimeRange(0, 5),
            new Dictionary<string, JsonElement>
            {
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d }),
            },
            new Dictionary<string, JsonElement>
            {
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new { unit = "Hz" }),
            },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new[] { new[] { 9d, 8d, 7d } }),
            },
            new Dictionary<string, JsonElement>
            {
                ["psd"] = JsonSerializer.SerializeToElement(new { unit = "V^2/Hz" }),
            },
            [
                JsonSerializer.SerializeToElement(new
                {
                    window_id = "full-window-1", window_index = 0, start_sample = 0,
                    end_sample = 1000, state = "Complete", quality = "clean",
                })
            ],
            new Dictionary<string, int> { ["Complete"] = 1 },
            JsonSerializer.SerializeToElement("clean"), "fixture-v1", "fixture-v1");

    internal static StructuredPreviewResponse FullStftStructuredPreview() =>
        new(
            "full-run-1",
            new RunArtifact("artifact", "full-run-1", "official_algorithm", "artifact.npz",
                "application/npz", 1, "sha256", "V^2/Hz",
                new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
            JsonSerializer.SerializeToElement(new { kind = "time_frequency" }),
            ["F3"],
            new TimeRange(0, 30),
            new TimeRange(0, 30),
            new Dictionary<string, JsonElement>
            {
                ["time_center_s"] = JsonSerializer.SerializeToElement(new[] { 2d, 3d }),
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 10d, 11d }),
            },
            new Dictionary<string, JsonElement>
            {
                ["time_center_s"] = JsonSerializer.SerializeToElement(new { unit = "s" }),
                ["frequency_hz"] = JsonSerializer.SerializeToElement(new { unit = "Hz" }),
            },
            new Dictionary<string, JsonElement>
            {
                ["power_linear"] = JsonSerializer.SerializeToElement(new[]
                {
                    new[] { new[] { 1e-12, 2e-12 }, new[] { 2e-12, 3e-12 } },
                    new[] { new[] { 2e-12, 3e-12 }, new[] { 3e-12, 4e-12 } },
                    new[] { new[] { 3e-12, 4e-12 }, new[] { 4e-12, 5e-12 } },
                }),
                ["power_db"] = JsonSerializer.SerializeToElement(new[]
                {
                    new[] { new[] { 1d, 2d }, new[] { 2d, 3d } },
                    new[] { new[] { 2d, 3d }, new[] { 3d, 4d } },
                    new[] { new[] { 3d, 4d }, new[] { 4d, 5d } },
                }),
            },
            new Dictionary<string, JsonElement>
            {
                ["power_linear"] = JsonSerializer.SerializeToElement(new { unit = "V^2/Hz" }),
                ["power_db"] = JsonSerializer.SerializeToElement(new { unit = "dB re 1 uV^2/Hz" }),
            },
            [
                JsonSerializer.SerializeToElement(new
                {
                    window_id = "full-window-0", window_index = 0, start_sample = 0,
                    end_sample = 1000, start_s = 0d, end_s = 5d, state = "Complete", quality = "clean",
                    provenance = new
                    {
                        preprocessing_scope = "whole_recording_before_window_slice",
                        inner_time_axis = "relative_window_time_center_s", analysis_state = "Complete",
                    },
                }),
                JsonSerializer.SerializeToElement(new
                {
                    window_id = "full-window-1", window_index = 1, start_sample = 1000,
                    end_sample = 2000, start_s = 5d, end_s = 10d, state = "Complete", quality = "clean",
                    provenance = new
                    {
                        preprocessing_scope = "whole_recording_before_window_slice",
                        inner_time_axis = "relative_window_time_center_s", analysis_state = "Complete",
                    },
                }),
                JsonSerializer.SerializeToElement(new
                {
                    window_id = "full-window-2", window_index = 2, start_sample = 2000,
                    end_sample = 3000, start_s = 10d, end_s = 15d, state = "Complete", quality = "clean",
                    provenance = new
                    {
                        preprocessing_scope = "whole_recording_before_window_slice",
                        inner_time_axis = "relative_window_time_center_s", analysis_state = "Complete",
                    },
                }),
            ],
            new Dictionary<string, int> { ["Complete"] = 3 },
            JsonSerializer.SerializeToElement("clean"), "fixture-v1", "fixture-v1");

    private static AnalysisRunResponse CompletedFullRun(AnalysisRunRequest request) => new(
        "full-run-1", request.RecordingId, request.AnalysisType, "completed", "fixture-v1", null, null,
        null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10, "completed", "已完成");

    private static AnalysisRunResponse ThrowFullRun(AnalysisRunRequest request) => throw new NotSupportedException();
}

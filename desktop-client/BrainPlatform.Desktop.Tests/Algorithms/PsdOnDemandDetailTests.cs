using System.Reflection;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[CollectionDefinition("OnDemandWindowFeature", DisableParallelization = true)]
public sealed class OnDemandWindowFeatureCollection { }

[Collection("OnDemandWindowFeature")]
public sealed class PsdOnDemandDetailTests : IDisposable
{
    private readonly string? previousFlag = Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_PSD");
    public PsdOnDemandDetailTests() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_PSD", "1");
    public void Dispose() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_PSD", previousFlag);

    [Fact]
    public async Task Timeline_is_ready_before_first_window_and_never_creates_a_full_run()
    {
        var client = new WindowClient();
        var catalog = ReadyCatalog(client);
        await InvokeRun(catalog);
        Assert.True(catalog.IsOnDemandAnalysis);
        Assert.Null(catalog.LastRun);
        Assert.False(catalog.HasPsdPreview);
        Assert.Equal("insufficient_data", catalog.DisplayedAnalysisWindow!.State);
        Assert.True(catalog.HasDynamicPreview);
        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        Assert.Equal(0, catalog.DynamicPreviewCursorSeconds);
        Assert.Contains("30", catalog.DynamicTimeProgressText);
        catalog.StartDynamicPreviewCommand.Execute(null);
        Assert.True(catalog.IsDynamicPreviewPlaying);
        catalog.PauseDynamicPreviewCommand.Execute(null);
        Assert.False(catalog.IsDynamicPreviewPlaying);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(1200, client.Requests[^1].CursorSample);
        Assert.True(catalog.HasPsdPreview);
        Assert.Equal(4e12, catalog.PsdPreviewPoints[0]!.Value);
        Assert.Equal(0.6, catalog.PsdBandSharePoints[2].Share);
        Assert.Null(catalog.PsdStructuredPreview);
        Assert.Null(catalog.LastRun);
        Assert.Contains("已显示", catalog.DynamicCurrentWindowText);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Backend_actual_range_and_quantitative_quality_failure_are_visible()
    {
        var client = new WindowClient { Reject = true };
        var catalog = ReadyCatalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Psd;
        await InvokeRun(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.False(detail.HasStructuredResult);
        Assert.True(detail.HasFailureDetails);
        Assert.Equal(1, detail.RejectedWindowCount);
        Assert.Contains("350", detail.FailureDetailsText);
        Assert.Contains("200", detail.FailureDetailsText);
        Assert.Contains("PSD_WINDOW_QUALITY_GATE_FAILED", detail.FailureReasonText);
        Assert.Contains("窗口", detail.RunIdText);
        Assert.NotEqual("未运行", detail.ActualRangeText);
        Assert.Contains("质量拒绝", detail.WindowStateSummary);
        Assert.Null(detail.LastRun);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Scientific_config_change_clears_old_projection_and_next_request_uses_new_parameters()
    {
        var client = new WindowClient();
        var catalog = ReadyCatalog(client);
        await InvokeRun(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.True(catalog.HasPsdPreview);
        catalog.LowFrequencyText = "2";
        Assert.False(catalog.IsOnDemandAnalysis);
        Assert.False(catalog.HasPsdPreview);
        Assert.Null(catalog.DisplayedAnalysisWindow);
        await InvokeRun(catalog);
        Assert.Equal(2, client.Requests[^1].Config.GetProperty("low_hz").GetDouble());
        Assert.Null(client.Requests[^1].DefinitionVersion);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Close_and_reopen_keeps_cursor_but_releases_and_reacquires_observation()
    {
        var client = new WindowClient();
        var catalog = ReadyCatalog(client);
        await InvokeRun(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        await catalog.ReleaseWindowAnalysisAsync();
        var count = client.Requests.Count;
        catalog.ResumeWindowAnalysis();
        await catalog.CurrentWindowLoad;
        Assert.Equal(count + 1, client.Requests.Count);
        Assert.Equal(1200, client.Requests[^1].CursorSample);
        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        Assert.True(catalog.HasPsdPreview);
        Assert.Equal(client.Requests.Count, client.Released);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Flag_off_retains_legacy_full_run_path()
    {
        Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_PSD", "0");
        var client = new WindowClient();
        var catalog = ReadyCatalog(client);
        await Assert.ThrowsAsync<NotSupportedException>(() => InvokeRun(catalog));
        Assert.Equal(1, client.FullRunCalls);
        Assert.Empty(client.Requests);
        Assert.False(catalog.IsOnDemandAnalysis);
    }

    [Fact]
    public void Dynamic_execution_defaults_to_full_and_allows_explicit_on_demand_selection()
    {
        var catalog = ReadyCatalog(new WindowClient(), onDemand: false);

        Assert.Equal("全量分析", catalog.SelectedDynamicExecutionMode);
        Assert.Equal(["全量分析", "按需分析", "按需预览 + 后台全量"], catalog.DynamicExecutionModes);
        Assert.False(catalog.IsOnDemandExecutionModeSelected);

        catalog.SelectedDynamicExecutionMode = "按需分析";

        Assert.True(catalog.IsOnDemandExecutionModeSelected);
    }

    [Fact]
    public void Disabled_on_demand_feature_exposes_only_full_execution_and_rejects_selection()
    {
        Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_PSD", "0");
        var catalog = ReadyCatalog(new WindowClient(), onDemand: false);

        Assert.Equal(["全量分析"], catalog.DynamicExecutionModes);
        catalog.SelectedDynamicExecutionMode = "按需分析";

        Assert.Equal("全量分析", catalog.SelectedDynamicExecutionMode);
        Assert.False(catalog.IsOnDemandExecutionModeSelected);
    }

    private static AlgorithmListViewModel ReadyCatalog(WindowClient client, bool onDemand = true)
    {
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter())
        {
            SelectedAlgorithm = new AlgorithmCatalogItem("official", "psd", "offline-spectral-v3",
                "功率谱密度", "PSD", "", [], ["static", "dynamic"], default,
                new DynamicAnalysisPolicy(4, [5, 10], 5, 1, true), "available", true, null, null, null),
        };
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.RegisteredRecording))!
            .SetValue(catalog, new RegisteredRecording("recording", "fixture", ".wpf", "2026-10-13T00:00:00Z",
                200, 30, ["F3"], "hash"));
        catalog.RegisteredChannels.Add("F3");
        catalog.SelectedChannel = "F3";
        catalog.EndSecondsText = "30";
        catalog.SelectedAnalysisMode = "动态";
        catalog.SelectedDynamicExecutionMode = onDemand ? "按需分析" : "全量分析";
        return catalog;
    }

    private static Task InvokeRun(AlgorithmListViewModel catalog) =>
        (Task)typeof(AlgorithmListViewModel).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(catalog, null)!;

    private sealed class WindowClient : IAlgorithmClient
    {
        public List<DynamicWindowRequest> Requests { get; } = [];
        public bool Reject { get; init; }
        public int Released { get; private set; }
        public int FullRunCalls { get; private set; }

        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request, CancellationToken token)
        {
            Requests.Add(request);
            var state = request.CursorSample < 800 ? "insufficient_data" : Reject ? "rejected" : "ready";
            var failure = state == "ready" ? null : new DynamicWindowFailure(
                state == "rejected" ? "PSD_WINDOW_QUALITY_GATE_FAILED" : "PSD_WINDOW_INSUFFICIENT_DATA",
                state == "rejected" ? "质量门拒绝" : "数据不足",
                JsonSerializer.SerializeToElement(new { clean_segments = 0, total_segments = 1, clean_ratio = 0.0,
                    evidence = new { amplitude = new { peak_uv = 350, threshold_uv = 200, exceeded_uv = 150 } } }));
            var result = new DynamicWindowResultResponse(null, state, "frequency_series",
                new Dictionary<string, JsonElement> { ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d }) },
                new Dictionary<string, string> { ["frequency_hz"] = "Hz" },
                new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(new[] { 4d, 5d }),
                    ["band_share"] = JsonSerializer.SerializeToElement(new[] { 0.1, 0.1, 0.6, 0.15, 0.05 }) },
                new Dictionary<string, string> { ["psd"] = "V^2/Hz", ["band_share"] = "ratio" },
                state == "rejected" ? "gate_failed" : "clean", null,
                JsonSerializer.SerializeToElement(new { source_quality = new {
                    amplitude = new { peak_uv = 350, threshold_uv = 200, exceeded_uv = 150 },
                    clean_segments = 0, total_segments = 1, clean_ratio = 0.0 } }),
                JsonSerializer.SerializeToElement(new { preprocessing_scope = "whole_recording_before_window_slice" }));
            return Task.FromResult(new DynamicWindowResponse("session", $"window-{request.CursorSample}", request.RequestId!, state,
                JsonSerializer.SerializeToElement(new { recording_id = request.RecordingId }),
                new DynamicWindowPlanResponse(request.CursorSample, Math.Max(0, request.CursorSample - 1000),
                    request.CursorSample, Math.Max(0, request.CursorSample / 200d - 5), request.CursorSample / 200d, "planned", null),
                new DynamicWindowRange(Math.Max(0, request.CursorSample - 1000), request.CursorSample,
                    Math.Max(0, request.CursorSample / 200d - 5), request.CursorSample / 200d),
                result.Quality, failure, state == "ready" ? result : null, $"subscription-{request.CursorSample}"));
        }

        public Task<DynamicSubscriptionResponse> CancelDynamicSubscriptionAsync(string subscriptionId, string? projectId, CancellationToken token)
        {
            Released++;
            return Task.FromResult(new DynamicSubscriptionResponse(subscriptionId, "window", "request", projectId, "cancelled"));
        }
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken token)
        {
            FullRunCalls++;
            throw new NotSupportedException();
        }
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken token) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int cells, CancellationToken token) => throw new NotSupportedException();
    }
}

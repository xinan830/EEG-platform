using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class ThetaBetaDetailTests
{
    [Fact]
    public void QualityCard_UsesThetaBetaModule()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        var detail = new AlgorithmDetailViewModel(catalog);
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta", "Theta/Beta", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);
        Assert.True(detail.Quality.IsAvailable);
        Assert.Equal("尚未运行分析", detail.Quality.QualityText);
    }

    [Fact]
    public void DynamicHandler_PopulatesSharedTimelineForPlayback()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta", "Theta/Beta", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [10], 10, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";
        ThetaBetaRunResultHandler.ApplyDynamicWindows(catalog, Run(Json("""
            {"metric":{"official":{"algorithm_id":"theta_beta"},"output":{"unit":"dimensionless"},"channel":"O1","series":[
              {"time_s":5,"window_start_s":0,"window_end_s":5,"value":0.4,"analysis_state":"Complete","quality":{"status":"clean"}},
              {"time_s":6,"window_start_s":1,"window_end_s":6,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"质量门未通过"}}
            ]}}
            """)));
        Assert.True(catalog.HasDynamicPreview);
        Assert.Equal(5, catalog.DynamicPreviewCursorSeconds);
        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        catalog.DynamicPreviewCursorSecondsForTest(6);
        Assert.Equal(2, catalog.DynamicPreviewRows.Count);
    }

    [Fact]
    public void StaticResult_PreservesBackendEvidenceAndDoesNotRecompute()
    {
        var preview = Assert.IsType<ThetaBetaResultPreview>(ThetaBetaResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"theta_beta","theta_beta_evidence":{"iapf_hz":10.2,"theta_range_hz":[4.2,8.2],"beta_range_hz":[12.2,30],"theta_power_uv2":1.25,"beta_power_uv2":2.5}},"output":{"unit":"dimensionless","value":0.5},"channel":"O1","quality":{"status":"clean"},"calculation_trace":{"formula":"个体化 Theta 功率 ÷ 个体化 Beta 功率"}}}
            """)));
        Assert.Equal("O1", preview.Channel);
        Assert.Equal(0.5, preview.StaticPoint?.Value);
        Assert.Equal(10.2, preview.StaticPoint?.Evidence.IapfHz);
        Assert.Equal("4.2–8.2 Hz", Format(preview.StaticPoint?.Evidence.ThetaLowHz, preview.StaticPoint?.Evidence.ThetaHighHz));
        Assert.Equal(1.25, preview.StaticPoint?.Evidence.ThetaPowerUv2);
        Assert.Equal(2.5, preview.StaticPoint?.Evidence.BetaPowerUv2);
    }

    [Fact]
    public void DynamicResult_ReleasesRejectedWindowsAsGaps()
    {
        var preview = Assert.IsType<ThetaBetaResultPreview>(ThetaBetaResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"theta_beta"},"output":{"unit":"dimensionless"},"channel":"O1","series":[
              {"time_s":5,"window_start_s":0,"window_end_s":5,"value":0.4,"analysis_state":"Complete","quality":{"status":"clean"},"official":{"algorithm_id":"theta_beta","theta_beta_evidence":{"iapf_hz":10,"theta_range_hz":[4,8],"beta_range_hz":[12,30]}}},
              {"time_s":6,"window_start_s":1,"window_end_s":6,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"code":"PSD_QUALITY_GATE_FAILED","message":"当前窗口未通过 PSD 质量门"}}
            ]}}
            """)));
        Assert.Equal(2, preview.DynamicPoints.Count);
        Assert.Equal(0.4, preview.CurrentAt(5)?.Value);
        Assert.Null(preview.CurrentAt(6)?.Value);
        Assert.Contains("质量门", preview.CurrentAt(6)?.Failure);
    }

    private static string Format(double? low, double? high) => $"{low:0.##}–{high:0.##} Hz";
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run-1", "recording-1", "official_algorithm", "completed", "official-theta-beta-v2",
        new TimeRange(0, 6), new TimeRange(0, 6), summary, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class UnusedClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

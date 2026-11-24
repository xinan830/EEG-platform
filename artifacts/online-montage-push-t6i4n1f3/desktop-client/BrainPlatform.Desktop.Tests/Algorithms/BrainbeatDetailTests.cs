using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class BrainbeatDetailTests
{
    [Theory]
    [InlineData("Fz", "Pz")]
    [InlineData("Fz-REF", "Pz-REF")]
    public void Source_roles_accept_raw_or_verified_hardware_reference_outputs(string primary, string secondary)
    {
        var inputs = new BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared.AlgorithmRunInputs(
            new TimeRange(0, 10), primary, secondary, [primary, secondary], 500,
            "1", "50", "4", "8", "13", "30", "50 Hz");
        var parameters = new BrainbeatRunResultHandler().ParseParameters(inputs);
        Assert.Equal(secondary, parameters.F4Channel);
    }

    [Fact]
    public void DynamicHandler_PopulatesSharedTimelineAndQualitySource()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        var detail = new AlgorithmDetailViewModel(catalog);
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "brainbeat", "official-brainbeat-v2", "脑节律指标", "Brainbeat", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [5], 5, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";

        var run = Run(JsonDocument.Parse("""
            {"metric":{"output":{"unit":"ratio","value":null},"channel":"Fz/Pz","series":[{"time_s":5,"window_start_s":0,"window_end_s":5,"analysis_state":"Complete","quality":{"status":"clean"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":0.4,"unit":"ratio"}},{"time_s":6,"window_start_s":1,"window_end_s":6,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"当前窗口未通过 PSD 质量门"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":null,"unit":"ratio"}}],"actual_range":{"start_s":0,"end_s":30},"mode":"dynamic","result_contract_version":"dynamic-analysis-frame-v6"},"result_contract_version":"official-algorithm-result-v1"}
            """).RootElement.Clone());
        // Run orchestration publishes LastRun before delegating the timeline projection.
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.LastRun))!.SetValue(catalog, run);
        BrainbeatRunResultHandler.ApplyDynamicWindows(catalog, run);

        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        Assert.Equal(5, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(0, detail.Quality.RejectedWindowCount);
        detail.Quality.ToggleFailureDetailsCommand.Execute(null);
        Assert.True(detail.Quality.IsFailureDetailsExpanded);
        catalog.DynamicPreviewCursorSecondsForTest(6);
        Assert.Equal(1, detail.Quality.RejectedWindowCount);
        Assert.Equal(2, catalog.DynamicPreviewRows.Count);
    }

    [Theory]
    [InlineData("brainbeat", "经典风格 BrainBeat：固定频带绝对功率")]
    [InlineData("brainbeat_individualized", "个体化 BrainBeat：IAPF 相对功率")]
    public void Detail_module_keeps_variant_formula_identity_visible(string algorithmId, string label)
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        var detail = new AlgorithmDetailViewModel(catalog);
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", algorithmId, "fixture-v1", label, label, "", [],
            ["static", "dynamic"], default, new DynamicAnalysisPolicy(10, [10, 20, 30], 20, 1, false),
            "available", true, null, null, null);

        var brainbeat = Assert.IsType<BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Brainbeat.BrainbeatDetailViewModel>(detail.CurrentModule);
        Assert.Contains(label, brainbeat.VariantText);
        Assert.Contains("Fz", brainbeat.FormulaText);
        Assert.Contains("Pz", brainbeat.FormulaText);
    }

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run-1", "recording-1", "official_algorithm", "completed", "official-brainbeat-v2",
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

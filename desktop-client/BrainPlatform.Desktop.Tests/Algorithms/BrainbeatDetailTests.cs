using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class BrainbeatDetailTests
{
    [Fact]
    public void DynamicHandler_PopulatesSharedTimelineAndQualitySource()
    {
        var catalog = new AlgorithmListViewModel(new UnusedClient(), new OperationNotificationCenter());
        var detail = new AlgorithmDetailViewModel(catalog);
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "brainbeat", "official-brainbeat-v1", "脑节律指标", "Brainbeat", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [5], 5, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";

        BrainbeatRunResultHandler.ApplyDynamicWindows(catalog, Run(JsonDocument.Parse("""
            {"metric":{"output":{"unit":"ratio"},"channel":"Fz/Pz","series":[
              {"time_s":5,"window_start_s":0,"window_end_s":5,"value":0.4,"analysis_state":"Complete","quality":{"status":"clean"}},
              {"time_s":6,"window_start_s":1,"window_end_s":6,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"当前窗口未通过 PSD 质量门"}}
            ]}}
            """).RootElement.Clone()));

        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        Assert.Equal(5, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(1, detail.Quality.RejectedWindowCount);
        detail.Quality.ToggleFailureDetailsCommand.Execute(null);
        Assert.True(detail.Quality.IsFailureDetailsExpanded);
        catalog.DynamicPreviewCursorSecondsForTest(6);
        Assert.Equal(2, catalog.DynamicPreviewRows.Count);
    }

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run-1", "recording-1", "official_algorithm", "completed", "official-brainbeat-v1",
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

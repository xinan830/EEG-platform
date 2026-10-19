using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class FullResultFallbackTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("artifact_run")]
    [InlineData("version")]
    [InlineData("truncated")]
    [InlineData("rank")]
    [InlineData("range")]
    [InlineData("order")]
    [InlineData("empty")]
    public async Task Invalid_structured_cache_tries_indexed_read_without_computing(string defect)
    {
        var preview = OnDemandDetailClient.FullPsdStructuredPreview();
        preview = defect switch
        {
            "run" => preview with { RunId = "stale" },
            "artifact_run" => preview with { Artifact = preview.Artifact with { RunId = "stale" } },
            "version" => preview with { ScientificVersion = "old" },
            "truncated" => preview with { Arrays = Arrays(Array.Empty<double[]>()) },
            "rank" => preview with { Arrays = Arrays(new[] { 9d, 8d, 7d }) },
            "range" => preview with { Windows = [Window(0, 1000, 0)] },
            "order" => preview with { Windows = [Window(0, 0, 1000), Window(1, 0, 500)] },
            "empty" => preview with { Windows = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var client = Client();
        client.RespondFullStructuredPreview = _ => preview;
        var catalog = OnDemandDetailClient.Catalog(client, "psd", "V^2/Hz");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";
        try
        {
            await OnDemandDetailClient.Run(catalog);
            await catalog.CurrentWindowLoad;
            var computations = client.Requests.Count;
            catalog.DynamicTimeProgressPercent = 50;
            await catalog.CurrentWindowLoad;

            Assert.Equal(1, client.StructuredPreviewCalls);
            Assert.True(client.FullWindowPreviewCalls > 0);
            Assert.Equal(computations, client.Requests.Count);
            Assert.Equal("全量已完成窗口", catalog.DynamicPreviewSourceText);
            Assert.StartsWith("full:", catalog.DisplayedAnalysisWindow!.SessionId);
            Assert.Equal(new[] { 9e12, 8e12, 7e12 }, catalog.PsdPreviewState.Points.Select(p => p!.Value));
        }
        finally { await catalog.ReleaseWindowAnalysisAsync(); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("run")]
    [InlineData("recording")]
    [InlineData("version")]
    [InlineData("cursor")]
    [InlineData("future")]
    [InlineData("array")]
    public async Task Unusable_indexed_response_explicitly_falls_back_to_on_demand(string defect)
    {
        var client = Client();
        client.RespondFullStructuredPreview = _ => throw new InvalidOperationException("incomplete artifact");
        client.RespondFullWindowPreview = (_, sample) =>
        {
            var preview = OnDemandDetailClient.FullPsdWindowPreview(sample);
            return defect switch
            {
                "missing" => throw new InvalidOperationException("missing index"),
                "run" => preview with { RunId = "stale" },
                "recording" => preview with { RecordingId = "other" },
                "version" => preview with { ScientificVersion = "old" },
                "cursor" => preview with { CursorSample = sample + 1 },
                "future" => preview with { Window = preview.Window! with { EndSample = sample + 1 } },
                "array" => preview with { Arrays = Arrays(new[] { 9d, 8d }) },
                _ => throw new ArgumentOutOfRangeException(nameof(defect)),
            };
        };
        var catalog = OnDemandDetailClient.Catalog(client, "psd", "V^2/Hz");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";
        try
        {
            await OnDemandDetailClient.Run(catalog);
            await catalog.CurrentWindowLoad;
            catalog.DynamicTimeProgressPercent = 50;
            await catalog.CurrentWindowLoad;
            Assert.Equal("按需窗口（全量窗口不可用）", catalog.DynamicPreviewSourceText);
            Assert.Equal("session", catalog.DisplayedAnalysisWindow!.SessionId);
            Assert.Equal("ready", catalog.DisplayedAnalysisWindow.State);
            Assert.Equal(new[] { 1e12, 2e12, 1e12 }, catalog.PsdPreviewState.Points.Select(p => p!.Value));
        }
        finally { await catalog.ReleaseWindowAnalysisAsync(); }
    }

    [Theory]
    [InlineData("recording")]
    [InlineData("version")]
    [InlineData("type")]
    public async Task Mismatched_completed_run_is_not_used_for_any_full_read(string defect)
    {
        var client = Client();
        var respond = client.RespondFullRun;
        client.RespondFullRun = request => defect switch
        {
            "recording" => respond(request) with { RecordingId = "other" },
            "version" => respond(request) with { ScientificVersion = "old" },
            _ => respond(request) with { AnalysisType = "other" },
        };
        var catalog = OnDemandDetailClient.Catalog(client, "psd", "V^2/Hz");
        catalog.SelectedDynamicExecutionMode = "按需预览 + 后台全量";
        try
        {
            await OnDemandDetailClient.Run(catalog);
            await catalog.CurrentWindowLoad;
            catalog.DynamicTimeProgressPercent = 50;
            await catalog.CurrentWindowLoad;
            Assert.Equal(0, client.StructuredPreviewCalls);
            Assert.Equal(0, client.FullWindowPreviewCalls);
            Assert.Equal("session", catalog.DisplayedAnalysisWindow!.SessionId);
        }
        finally { await catalog.ReleaseWindowAnalysisAsync(); }
    }

    private static OnDemandDetailClient Client() => new()
    {
        Respond = OnDemandDetailClient.PsdResponse,
        RespondFullRun = request => new AnalysisRunResponse("full-run-1", request.RecordingId,
            request.AnalysisType, "completed", "fixture-v1", null, null, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
    };

    private static IReadOnlyDictionary<string, JsonElement> Arrays<T>(T values) =>
        new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(values) };

    private static JsonElement Window(int index, long start, long end) => JsonSerializer.SerializeToElement(new
    {
        window_id = $"window-{index}", window_index = index, start_sample = start,
        end_sample = end, state = "Complete", quality = "clean",
    });
}

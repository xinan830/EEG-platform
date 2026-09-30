using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class StftPreviewTests
{
    [Fact]
    public void StaticStftConfig_PreservesBackendIdentityAndRequestedInputs()
    {
        var algorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v2", "时频分析", "STFT", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [], 4, 1, false), "available", true, null, null, null);

        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(algorithm, "O1", new TimeRange(1.25, 9.875));

        Assert.Equal("stft", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("spectrogram-v2", config.GetProperty("scientific_version").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
        Assert.Equal(1.25, config.GetProperty("time").GetProperty("start_s").GetDouble());
        Assert.Equal(9.875, config.GetProperty("time").GetProperty("end_s").GetDouble());
    }

    [Fact]
    public void Parse_PreservesTimeMajorOrientationAndNullCells()
    {
        var result = StftPreview.Parse(Preview("[[1,null],[3,4]]"));

        Assert.Equal([2.0, 3.0], result.TimesSeconds);
        Assert.Equal([1.0, 2.0], result.FrequenciesHz);
        Assert.Equal(1, result.PowerDb[0][0]);
        Assert.Null(result.PowerDb[0][1]);
        Assert.Equal(3, result.PowerDb[1][0]);
        Assert.Equal(1, result.MinimumDb);
        Assert.Equal(4, result.MaximumDb);
    }

    [Fact]
    public void Parse_RejectsWrongMatrixShapeAndUnit()
    {
        Assert.Throws<InvalidOperationException>(() => StftPreview.Parse(Preview("[[1,2]]")));
        Assert.Throws<InvalidOperationException>(() => StftPreview.Parse(Preview("[[1,2],[3,4]]", "V^2/Hz")));
        Assert.Throws<InvalidOperationException>(() => StftPreview.Parse(Preview("[[1,2],[3,4]]", windows: [Json("{}")])));
    }

    [Fact]
    public void Parse_RejectsUnavailableResultWithoutInventingZero()
    {
        Assert.Throws<InvalidOperationException>(() => StftPreview.Parse(Preview("[[null,null],[null,null]]")));
    }

    [Fact]
    public void ParseDynamic_UsesSelectedWindowAndAbsoluteTime()
    {
        var preview = DynamicPreview("[[[null,null],[null,null]],[[1,null],[3,4]],[[5,6],[7,8]]]");

        var second = StftPreview.ParseDynamic(preview, 1);
        var third = StftPreview.ParseDynamic(preview, 2);

        Assert.Equal([4.0, 5.0], second.TimesSeconds);
        Assert.Equal([5.0, 6.0], third.TimesSeconds);
        Assert.Equal(1, second.PowerDb[0][0]);
        Assert.Null(second.PowerDb[0][1]);
        Assert.Equal(8, third.PowerDb[1][1]);
        Assert.Equal(new TimeRange(3, 7), third.RequestedRange);
    }

    [Fact]
    public void ParseDynamic_DoesNotDisplayRejectedOrMalformedWindow()
    {
        var preview = DynamicPreview("[[[null,null],[null,null]],[[1,2],[3,4]],[[5,6],[7,8]]]");

        Assert.Throws<InvalidOperationException>(() => StftPreview.ParseDynamic(preview, 0));
        Assert.Throws<InvalidOperationException>(() => StftPreview.ParseDynamic(preview, 3));
        Assert.Throws<InvalidOperationException>(() => StftPreview.ParseDynamic(
            DynamicPreview("[[[null,null],[null,null]],[[1,2]],[[5,6],[7,8]]]"), 1));
    }

    [Fact]
    public async Task StaticPreview_LoadsThroughStftOwnedCatalogPath()
    {
        var catalog = new AlgorithmListViewModel(new PreviewClient(Preview("[[1,null],[3,4]]")), new OperationNotificationCenter());

        await catalog.LoadStructuredPreviewAsync("run-1", "stft");

        Assert.True(catalog.HasStftPreview);
        Assert.Equal(3, catalog.StftResult?.PowerDb[1][0]);
        Assert.Null(catalog.StftResult?.PowerDb[0][1]);
        Assert.Contains("时频中心", catalog.StftAxisText);
    }

    [Fact]
    public void ChartSelection_UsesStableKindRatherThanDisplayText()
    {
        var catalog = new AlgorithmListViewModel(new PreviewClient(Preview("[[1,2],[3,4]]")), new OperationNotificationCenter());
        var detail = new StftDetailViewModel(catalog);

        detail.SelectedChart = detail.ChartOptions.Single(option => option.Kind == StftChartKind.Spectrum);
        Assert.True(detail.IsSpectrumSelected);
        Assert.False(detail.IsHeatmapSelected);

        detail.SelectedChart = detail.ChartOptions.Single(option => option.Kind == StftChartKind.FrequencyTrend);
        Assert.True(detail.IsTrendSelected);
    }

    [Fact]
    public async Task DynamicTimeline_SelectsBackendWindowAndClearsRejectedImage()
    {
        var preview = DynamicPreview("[[[null,null],[null,null]],[[1,2],[3,4]],[[5,6],[7,8]]]");
        var catalog = new AlgorithmListViewModel(new PreviewClient(preview), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v2", "时频分析", "STFT", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [4], 4, 1, true), "available", true, null, null, null);
        catalog.SelectedAnalysisMode = "动态";

        await catalog.LoadStructuredPreviewAsync("run-1", "stft", dynamic: true);
        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(1, catalog.StftResult?.PowerDb[0][0]);

        catalog.DynamicPreviewCursorSecondsForTest(7);
        Assert.Equal(5, catalog.StftResult?.PowerDb[0][0]);

        catalog.DynamicPreviewCursorSecondsForTest(4);
        Assert.Null(catalog.StftResult);
    }

    [Fact]
    public async Task DynamicTimeline_EmptyWindowsRemainUnavailableWithoutPreviewError()
    {
        var catalog = new AlgorithmListViewModel(new PreviewClient(Preview("[]")), new OperationNotificationCenter());

        await catalog.LoadStructuredPreviewAsync("run-1", "stft", dynamic: true);

        Assert.Empty(catalog.DynamicWindowRows);
        Assert.Null(catalog.StftResult);
        Assert.DoesNotContain("结构化预览不可用", catalog.StructuredPreviewText);
    }

    private static StructuredPreviewResponse Preview(string matrix, string unit = "dB re 1 uV^2/Hz", IReadOnlyList<JsonElement>? windows = null) => new(
        "run-1", new RunArtifact("artifact", "run-1", "stft", "stft.npz", "application/octet-stream", 1, "sha", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(0, 20), new TimeRange(0, 20),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[2,3]"), ["frequency_hz"] = Json("[1,2]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json(matrix) },
        new Dictionary<string, JsonElement> { ["power_db"] = JsonSerializer.SerializeToElement(new { unit }) },
        windows ?? [], new Dictionary<string, int>(), null, "spectrogram-v2", "stft-runtime-v1");

    private static StructuredPreviewResponse DynamicPreview(string matrix) => Preview(matrix, windows:
    [
        Json("{\"start_s\":0,\"end_s\":4,\"state\":\"Rejected\"}"),
        Json("{\"start_s\":2,\"end_s\":6,\"state\":\"Complete\"}"),
        Json("{\"start_s\":3,\"end_s\":7,\"state\":\"Complete\"}"),
    ]);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class PreviewClient(StructuredPreviewResponse preview) : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => Task.FromResult(preview);
    }
}

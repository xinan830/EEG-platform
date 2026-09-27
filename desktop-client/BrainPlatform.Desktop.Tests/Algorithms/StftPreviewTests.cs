using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class StftPreviewTests
{
    [Fact]
    public void StaticStftConfig_PreservesBackendIdentityAndRequestedInputs()
    {
        var algorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v2", "时频分析", "STFT", "", [], ["static", "dynamic"], default,
            new DynamicAnalysisPolicy(4, [], 4, 1, false), "available", true, null, null, null);

        var config = AlgorithmListViewModel.BuildStaticRunConfig(algorithm, "O1", new TimeRange(1.25, 9.875));

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

    private static StructuredPreviewResponse Preview(string matrix, string unit = "dB re 1 uV^2/Hz", IReadOnlyList<JsonElement>? windows = null) => new(
        "run-1", new RunArtifact("artifact", "run-1", "stft", "stft.npz", "application/octet-stream", 1, "sha", null, new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        Json("{\"kind\":\"time_frequency\"}"), ["O1"], new TimeRange(0, 20), new TimeRange(0, 20),
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("[2,3]"), ["frequency_hz"] = Json("[1,2]") },
        new Dictionary<string, JsonElement> { ["time_center_s"] = Json("{\"unit\":\"s\"}"), ["frequency_hz"] = Json("{\"unit\":\"Hz\"}") },
        new Dictionary<string, JsonElement> { ["power_db"] = Json(matrix) },
        new Dictionary<string, JsonElement> { ["power_db"] = JsonSerializer.SerializeToElement(new { unit }) },
        windows ?? [], new Dictionary<string, int>(), null, "spectrogram-v2", "stft-runtime-v1");

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}

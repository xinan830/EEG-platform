using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class FullRunMetricWindowPreviewAdapterTests
{
    [Fact]
    public void Projects_scalar_series_point_with_backend_evidence()
    {
        var run = Run(JsonSerializer.SerializeToElement(new
        {
            result_contract_version = "official-algorithm-result-v1",
            metric = new
            {
                mode = "dynamic", result_contract_version = "dynamic-analysis-frame-v6",
                actual_range = new { start_s = 0d, end_s = 30d },
                output = new { value = (double?)null, unit = "Hz" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d,
                        result_contract_version = "dynamic-analysis-frame-v6",
                        output = new { value = 10d, unit = "Hz" },
                        analysis_state = "Complete", quality = new { status = "clean" },
                        calculation_trace = new { formula = "peak" },
                        official = new { algorithm_id = "peak_frequency", peak_frequency_evidence = new { selected_power_v2_per_hz = 2d } },
                    },
                },
            },
        }));
        var target = Target(1200);

        Assert.True(FullRunMetricWindowPreviewAdapter.TryCreate(run, target, 200, "peak_frequency_hz", null, out var response));
        Assert.Equal("ready", response.State);
        Assert.Equal("full-metric:run", response.SessionId);
        Assert.Equal(5, response.Range!.EndSeconds);
        Assert.Equal(10d, response.Result!.Arrays!["peak_frequency_hz"][0].GetDouble());
        Assert.Equal("Hz", response.Result.ArrayUnits!["peak_frequency_hz"]);
        Assert.Equal("Complete", response.Result.Provenance!.Value.GetProperty("analysis_state").GetString());
        Assert.Equal(2d, response.Result.Evidence!.Value.GetProperty("extensions").GetProperty("peak_frequency_evidence").GetProperty("selected_power_v2_per_hz").GetDouble());
    }

    [Fact]
    public void Projects_multi_metric_band_values_without_recomputing_them()
    {
        var run = Run(JsonSerializer.SerializeToElement(new
        {
            result_contract_version = "official-algorithm-result-v1",
            metric = new
            {
                mode = "dynamic", result_contract_version = "dynamic-analysis-frame-v6",
                actual_range = new { start_s = 0d, end_s = 30d },
                output = new { value = (double?)null, unit = "ratio" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d,
                        result_contract_version = "dynamic-analysis-frame-v6",
                        output = new { value = (double?)null, unit = "ratio" },
                        analysis_state = "Complete", quality = new { status = "clean" },
                        band_values = new { delta = 0.1, theta = 0.2, alpha = 0.3, beta = 0.25, gamma = 0.15 },
                    },
                },
            },
        }));

        Assert.True(FullRunMetricWindowPreviewAdapter.TryCreate(run, Target(1200), 200, "ratio",
            ["delta", "theta", "alpha", "beta", "gamma"], out var response));
        Assert.Equal("multi_metric_series", response.Result!.OutputKind);
        Assert.Equal(0.3d, response.Result.Arrays!["alpha"][0].GetDouble());
        Assert.Equal("ratio", response.Result.ArrayUnits!["gamma"]);
    }

    [Fact]
    public void Marks_complete_multi_metric_window_unavailable_when_value_is_null()
    {
        var run = Run(JsonSerializer.SerializeToElement(new
        {
            result_contract_version = "official-algorithm-result-v1",
            metric = new
            {
                mode = "dynamic", result_contract_version = "dynamic-analysis-frame-v6",
                actual_range = new { start_s = 0d, end_s = 30d },
                output = new { value = (double?)null, unit = "ratio" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d,
                        result_contract_version = "dynamic-analysis-frame-v6",
                        output = new { value = (double?)null, unit = "ratio" },
                        analysis_state = "Complete", quality = new { status = "clean" },
                        band_values = new { delta = 0.1, theta = (double?)null, alpha = 0.3, beta = 0.25, gamma = 0.15 },
                    },
                },
            },
        }));

        Assert.True(FullRunMetricWindowPreviewAdapter.TryCreate(run, Target(1200), 200, "ratio",
            ["delta", "theta", "alpha", "beta", "gamma"], out var response));
        Assert.Equal("unavailable", response.State);
        Assert.Null(response.Result);
        Assert.Equal("MISSING_METRIC_VALUE", response.Failure!.Code);
    }

    [Fact]
    public void Does_not_turn_missing_or_rejected_values_into_success()
    {
        var run = Run(JsonSerializer.SerializeToElement(new
        {
            result_contract_version = "official-algorithm-result-v1",
            metric = new
            {
                mode = "dynamic", result_contract_version = "dynamic-analysis-frame-v6",
                actual_range = new { start_s = 0d, end_s = 30d },
                output = new { value = (double?)null, unit = "Hz" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d,
                        result_contract_version = "dynamic-analysis-frame-v6",
                        output = new { value = (double?)null, unit = "Hz" },
                        analysis_state = "Rejected", quality = new { status = "gate_failed" },
                        failure = new { code = "QUALITY", message = "rejected" },
                    },
                },
            },
        }));

        Assert.True(FullRunMetricWindowPreviewAdapter.TryCreate(run, Target(1200), 200, "peak_frequency_hz", null, out var response));
        Assert.Equal("rejected", response.State);
        Assert.Null(response.Result);
        Assert.Equal("QUALITY", response.Failure!.Code);
    }

    [Fact]
    public void Structured_preview_reports_insufficient_data_when_no_window_covers_cursor()
    {
        var preview = StructuredPreview([]);

        var response = FullRunWindowPreviewAdapter.ToDynamicWindowResponse(
            preview, StructuredTarget(1000), 200);

        Assert.Equal("insufficient_data", response.State);
        Assert.Null(response.Result);
        Assert.Equal("NO_COMPLETED_WINDOW", response.Failure!.Code);
    }

    [Fact]
    public void Structured_preview_rejects_malformed_window_metadata()
    {
        var preview = StructuredPreview([
            JsonSerializer.SerializeToElement(new
            {
                window_id = "window-0",
                start_sample = 0L,
                state = "Complete",
                quality = "clean",
            }),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FullRunWindowPreviewAdapter.ToDynamicWindowResponse(
                preview, StructuredTarget(1000), 200));

        Assert.Contains("end_sample", error.Message);
    }

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run", "recording", "official_algorithm", "completed", "fixture-v1", null, null,
        summary, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, "completed", "已完成");

    private static DynamicWindowRequest Target(long cursorSample) => new(
        "recording", "peak_frequency", null, null, JsonSerializer.SerializeToElement(new { }), cursorSample,
        RequestId: "request");

    private static DynamicWindowRequest StructuredTarget(long cursorSample) => new(
        "recording", "psd", null, null, JsonSerializer.SerializeToElement(new { }), cursorSample,
        RequestId: "request");

    private static StructuredPreviewResponse StructuredPreview(IReadOnlyList<JsonElement> windows) => new(
        "run",
        new RunArtifact("artifact", "run", "structured", "result.json", "application/json", 1, "hash", null,
            new Dictionary<string, int[]>(), DateTimeOffset.UtcNow),
        JsonSerializer.SerializeToElement(new { kind = "frequency_series" }),
        ["F3"],
        new TimeRange(0, 10),
        new TimeRange(0, 10),
        new Dictionary<string, JsonElement>
        {
            ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d, 3d }),
        },
        new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>
        {
            ["psd"] = JsonSerializer.SerializeToElement(new[] { new[] { 1d, 2d, 3d } }),
        },
        new Dictionary<string, JsonElement>(),
        windows,
        new Dictionary<string, int>(),
        JsonSerializer.SerializeToElement(new { status = "clean" }),
        "fixture-v1",
        "fixture-v1");
}

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
            metric = new
            {
                output = new { unit = "Hz" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d, value = 10d,
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
            metric = new
            {
                output = new { unit = "ratio" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d, value = (double?)null,
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
            metric = new
            {
                output = new { unit = "ratio" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d,
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
            metric = new
            {
                output = new { unit = "Hz" },
                series = new[]
                {
                    new
                    {
                        time_s = 5d, window_start_s = 0d, window_end_s = 5d, value = (double?)null,
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

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        "run", "recording", "official_algorithm", "completed", "fixture-v1", null, null,
        summary, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, "completed", "已完成");

    private static DynamicWindowRequest Target(long cursorSample) => new(
        "recording", "peak_frequency", null, null, JsonSerializer.SerializeToElement(new { }), cursorSample,
        RequestId: "request");
}

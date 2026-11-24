using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class CurrentAlgorithmResultTests
{
    [Fact]
    public void Current_nullable_value_is_preserved_without_fabricating_a_number()
    {
        var summary = Summary();
        CurrentAlgorithmResult.Require(Element(summary));
        Assert.Null(summary["metric"]!["series"]![0]!["output"]!["value"]);
    }

    [Theory]
    [InlineData("root_version")]
    [InlineData("frame_version")]
    [InlineData("duplicate_metric_value")]
    [InlineData("duplicate_point_value")]
    [InlineData("warmup")]
    [InlineData("state")]
    [InlineData("unit")]
    [InlineData("value")]
    [InlineData("start")]
    [InlineData("endpoint")]
    [InlineData("mode")]
    [InlineData("quality")]
    public void Unsupported_or_incomplete_results_are_rejected_without_conversion(string invalidField)
    {
        var summary = Summary();
        var metric = summary["metric"]!.AsObject();
        var point = metric["series"]![0]!.AsObject();
        switch (invalidField)
        {
            case "root_version": summary.Remove("result_contract_version"); break;
            case "frame_version": metric["result_contract_version"] = "dynamic-analysis-frame-v5"; break;
            case "duplicate_metric_value": metric["value"] = 10; break;
            case "duplicate_point_value": point["value"] = 10; break;
            case "warmup": point["warmup"] = true; break;
            case "state": point.Remove("analysis_state"); break;
            case "unit": point["output"]!.AsObject().Remove("unit"); break;
            case "value": point["output"]!.AsObject().Remove("value"); break;
            case "start": point.Remove("window_start_s"); break;
            case "endpoint": point["time_s"] = 6; break;
            case "mode": metric.Remove("mode"); break;
            case "quality": point.Remove("quality"); break;
        }
        var before = summary.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => CurrentAlgorithmResult.Require(Element(summary)));
        Assert.Equal(before, summary.ToJsonString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Http_run_boundary_validates_the_same_current_contract(bool current)
    {
        var summary = Summary();
        if (!current) summary.Remove("result_contract_version");
        using var client = new HttpClient(new RunHandler(Element(summary))) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);
        if (!current)
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetRunAsync("run", CancellationToken.None));
        else
        {
            var run = await api.GetRunAsync("run", CancellationToken.None);
            Assert.Equal("official-algorithm-result-v1", run.ResultSummary!.Value.GetProperty("result_contract_version").GetString());
        }
    }

    [Theory]
    [InlineData("current")]
    [InlineData("mode")]
    [InlineData("windows")]
    [InlineData("state")]
    [InlineData("quality")]
    [InlineData("warmup")]
    [InlineData("sample")]
    [InlineData("axis_key")]
    [InlineData("axis_length")]
    [InlineData("negative_shape")]
    [InlineData("boolean_shape")]
    [InlineData("unit")]
    public void Structured_result_requires_current_axes_arrays_and_window_evidence(string shape)
    {
        var summary = JsonNode.Parse("""
            {
              "result_contract_version": "official-algorithm-result-v1",
              "structured": {
                "output": { "kind": "psd", "mode": "dynamic" },
                "channel_order": ["Fz"],
                "requested_range": { "start_s": 0, "end_s": 30 },
                "actual_range": { "start_s": 0, "end_s": 30 },
                "axes": { "frequency_hz": { "array_key": "axis_frequency_hz", "unit": "Hz", "length": 2 } },
                "arrays": { "axis_frequency_hz": { "unit": "Hz", "shape": [2] }, "psd": { "unit": "uV^2/Hz", "shape": [1, 2] } },
                "windows": [{ "state": "Complete", "quality": "good", "start_sample": 0, "end_sample": 1000, "start_s": 0, "end_s": 5 }]
              }
            }
            """)!.AsObject();
        var structured = summary["structured"]!.AsObject();
        var window = structured["windows"]![0]!.AsObject();
        var axis = structured["axes"]!["frequency_hz"]!.AsObject();
        var array = structured["arrays"]!["psd"]!.AsObject();
        switch (shape)
        {
            case "mode": structured["output"]!.AsObject().Remove("mode"); break;
            case "windows": structured.Remove("windows"); break;
            case "state": window.Remove("state"); break;
            case "quality": window.Remove("quality"); break;
            case "warmup": window["warmup"] = true; break;
            case "sample": window["start_sample"] = 0.5; break;
            case "axis_key": axis["array_key"] = "missing"; break;
            case "axis_length": axis["length"] = 3; break;
            case "negative_shape": array["shape"]![0] = -1; break;
            case "boolean_shape": array["shape"]![0] = true; break;
            case "unit": array.Remove("unit"); break;
        }
        var before = summary.ToJsonString();
        if (shape == "current") CurrentAlgorithmResult.Require(Element(summary));
        else Assert.Throws<InvalidOperationException>(() => CurrentAlgorithmResult.Require(Element(summary)));
        Assert.Equal(before, summary.ToJsonString());
    }

    private static JsonObject Summary() => JsonNode.Parse("""
        {
          "result_contract_version": "official-algorithm-result-v1",
          "metric": {
            "mode": "dynamic", "result_contract_version": "dynamic-analysis-frame-v6",
            "actual_range": { "start_s": 0, "end_s": 30 },
            "output": { "value": null, "unit": "Hz" },
            "series": [{
              "result_contract_version": "dynamic-analysis-frame-v6",
              "time_s": 5, "window_start_s": 0, "window_end_s": 5,
              "analysis_state": "Unavailable", "quality": { "status": "unavailable" },
              "output": { "value": null, "unit": "Hz" }
            }]
          }
        }
        """)!.AsObject();

    private static JsonElement Element(JsonObject summary) => JsonSerializer.SerializeToElement(summary);

    private sealed class RunHandler(JsonElement summary) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    run_id = "run", recording_id = "recording", analysis_type = "official_algorithm",
                    status = "completed", scientific_version = "official-iapf-v2", result_summary = summary,
                    created_at = "2026-10-22T00:00:00Z", updated_at = "2026-10-22T00:00:01Z",
                }),
            });
    }
}

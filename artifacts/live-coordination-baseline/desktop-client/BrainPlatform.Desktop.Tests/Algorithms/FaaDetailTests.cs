using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class FaaDetailTests
{
    [Fact]
    public async Task Detail_created_after_window_response_binds_existing_window_immediately()
    {
        var flag = Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_FAA");
        Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_FAA", "1");
        try
        {
            var client = new OnDemandDetailClient { Respond = ReadyWindowResponse };
            var catalog = OnDemandDetailClient.Catalog(client, "faa");
            catalog.SelectedF4Channel = "F7";

            await OnDemandDetailClient.Run(catalog);
            catalog.DynamicTimeProgressPercent = 100;
            await catalog.CurrentWindowLoad;

            var detail = new AlgorithmDetailViewModel(catalog).Faa;

            Assert.True(detail.IsDynamicResult);
            Assert.Equal(0.25, detail.CurrentPoint?.Value);
            Assert.Equal("F3", detail.LeftChannelText);
            Assert.Equal("F4", detail.RightChannelText);
            Assert.Equal("良好", detail.QualityText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_FAA", flag);
        }
    }

    [Fact]
    public void StaticResult_PreservesPairedEvidence()
    {
        var preview = Assert.IsType<FaaResultPreview>(FaaResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"faa","faa_evidence":{"source_channels":{"left":"F3","right":"F4"},"p_f3":1e-12,"p_f4":2e-12,"clean_epochs":12,"total_epochs":14,"band":[8,13],"formula":"ln(P_right_alpha) - ln(P_left_alpha)"}},"output":{"unit":"dimensionless","value":0.693},"channel":"F3/F4","quality":{"status":"clean"},"actual_range":{"start_s":0,"end_s":30}},"result_contract_version":"official-algorithm-result-v1"}
            """)));
        Assert.Equal("F3/F4", preview.Channel);
        Assert.Equal("F3", preview.StaticPoint?.Evidence.LeftChannel);
        Assert.Equal("F4", preview.StaticPoint?.Evidence.RightChannel);
        Assert.Equal(1, preview.StaticPoint?.Evidence.LeftPowerUv2);
        Assert.Equal(2, preview.StaticPoint?.Evidence.RightPowerUv2);
        Assert.Equal("8–13 Hz", preview.StaticPoint?.Evidence.AlphaBand);
    }

    [Fact]
    public void DynamicResult_ReleasesRejectedPointWithoutInventingValue()
    {
        var preview = Assert.IsType<FaaResultPreview>(FaaResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"faa"},"output":{"unit":"dimensionless","value":null},"channel":"F3/F4","series":[{"time_s":20,"window_start_s":0,"window_end_s":20,"analysis_state":"Complete","quality":{"status":"clean"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":0.2,"unit":"dimensionless"}},{"time_s":21,"window_start_s":1,"window_end_s":21,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"当前范围未达到 FAA 成对 epoch 质量要求"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":null,"unit":"dimensionless"}}],"actual_range":{"start_s":0,"end_s":30},"mode":"dynamic","result_contract_version":"dynamic-analysis-frame-v6"},"result_contract_version":"official-algorithm-result-v1"}
            """)));
        Assert.Equal(0.2, preview.CurrentAt(20)?.Value);
        Assert.Null(preview.CurrentAt(21)?.Value);
        Assert.Contains("成对 epoch", preview.CurrentAt(21)?.Failure);
    }

    [Fact]
    public void OnDemandWindow_PreservesScalarValueAndContinuousPreprocessingEvidence()
    {
        var response = new DynamicWindowResponse(
            "session", "window", "request", "ready", null,
            new DynamicWindowPlanResponse(5000, 0, 5000, 0, 20, "planned", null),
            new DynamicWindowRange(0, 5000, 0, 20), "clean", null,
            new DynamicWindowResultResponse(
                null, "ready", "scalar_series",
                new Dictionary<string, JsonElement> { ["time_s"] = Json("[20]" ) },
                new Dictionary<string, string> { ["time_s"] = "s" },
                new Dictionary<string, JsonElement> { ["faa"] = Json("[0.25]") },
                new Dictionary<string, string> { ["faa"] = "dimensionless" },
                "clean", null,
                Json("{\"extensions\":{\"faa_evidence\":{\"source_channels\":{\"left\":\"F3\",\"right\":\"F4\"},\"p_f3\":1e-12,\"p_f4\":2e-12,\"band\":[8,13],\"formula\":\"ln(P_right_alpha) - ln(P_left_alpha)\"}},\"source_quality\":{\"clean_segments\":18,\"total_segments\":19},\"calculation_trace\":{}}"),
                Json("{\"preprocessing_scope\":\"whole_recording_before_window_slice\",\"paired_sources\":true,\"analysis_state\":\"Complete\"}")), null);

        var preview = FaaResultPreview.FromWindow(response);

        Assert.Equal(0.25, preview.DynamicPoints.Single().Value);
        Assert.Equal("F3", preview.DynamicPoints.Single().Evidence.LeftChannel);
        Assert.Equal("F4", preview.DynamicPoints.Single().Evidence.RightChannel);
    }

    [Fact]
    public void OnDemandReadyWindow_RejectsMismatchedAxisUnitOrMissingProvenance()
    {
        var response = ReadyResponse();
        Assert.Throws<InvalidOperationException>(() => FaaResultPreview.FromWindow(response with
        {
            Result = response.Result! with
            {
                ArrayUnits = new Dictionary<string, string> { ["faa"] = "ratio" }
            }
        }));
        Assert.Throws<InvalidOperationException>(() => FaaResultPreview.FromWindow(response with
        {
            Result = response.Result! with
            {
                Axes = new Dictionary<string, JsonElement> { ["time_s"] = Json("[19]") }
            }
        }));
        Assert.Throws<InvalidOperationException>(() => FaaResultPreview.FromWindow(response with
        {
            Result = response.Result! with { Provenance = Json("{\"paired_sources\":true}") }
        }));
    }

    [Fact]
    public void OnDemandRejectedWindow_PreservesStructuredFailureDetails()
    {
        var response = ReadyResponse() with
        {
            State = "rejected",
            Quality = "gate_failed",
            Result = null,
            Failure = new DynamicWindowFailure("FAA_WINDOW_QUALITY_GATE_FAILED", "质量门失败", Json("{\"clean_epochs\":3,\"minimum_clean_epochs\":10}")),
        };
        var preview = FaaResultPreview.FromWindow(response);
        Assert.Contains("clean_epochs", preview.DynamicPoints.Single().Failure);
        Assert.Null(preview.DynamicPoints.Single().Value);
        Assert.Equal("Rejected", preview.DynamicPoints.Single().State);
    }

    private static DynamicWindowResponse ReadyResponse() => new(
        "session", "window", "request", "ready", null,
        new DynamicWindowPlanResponse(5000, 0, 5000, 0, 20, "planned", null),
        new DynamicWindowRange(0, 5000, 0, 20), "clean", null,
        new DynamicWindowResultResponse(null, "ready", "scalar_series",
            new Dictionary<string, JsonElement> { ["time_s"] = Json("[20]") },
            new Dictionary<string, string> { ["time_s"] = "s" },
            new Dictionary<string, JsonElement> { ["faa"] = Json("[0.25]") },
            new Dictionary<string, string> { ["faa"] = "dimensionless" }, "clean", null,
            Json("{\"extensions\":{\"faa_evidence\":{\"source_channels\":{\"left\":\"F3\",\"right\":\"F4\"}}},\"calculation_trace\":{},\"source_quality\":{}}"),
            Json("{\"preprocessing_scope\":\"whole_recording_before_window_slice\",\"analysis_state\":\"Complete\",\"paired_sources\":true}")), null);

    private static DynamicWindowResponse ReadyWindowResponse(DynamicWindowRequest request) =>
        ReadyResponse() with
        {
            Identity = Json("{\"recording_id\":\"recording\"}"),
            Range = new DynamicWindowRange(0, 4000, 0, 20),
            Result = ReadyResponse().Result! with
            {
                Axes = new Dictionary<string, JsonElement> { ["time_s"] = Json("[20]") },
            },
        };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}

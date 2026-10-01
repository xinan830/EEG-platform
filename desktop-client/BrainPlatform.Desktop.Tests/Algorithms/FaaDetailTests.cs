using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class FaaDetailTests
{
    [Fact]
    public void StaticResult_PreservesPairedEvidence()
    {
        var preview = Assert.IsType<FaaResultPreview>(FaaResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"faa","faa_evidence":{"source_channels":{"left":"F3","right":"F4"},"p_f3":0.000000000001,"p_f4":0.000000000002,"clean_epochs":12,"total_epochs":14,"band":[8,13],"formula":"ln(P_right_alpha) - ln(P_left_alpha)"}},"output":{"unit":"dimensionless","value":0.693},"channel":"F3/F4","quality":{"status":"clean"}}}
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
            {"metric":{"official":{"algorithm_id":"faa"},"output":{"unit":"dimensionless"},"channel":"F3/F4","series":[
              {"time_s":20,"window_start_s":0,"window_end_s":20,"value":0.2,"analysis_state":"Complete","quality":{"status":"clean"}},
              {"time_s":21,"window_start_s":1,"window_end_s":21,"value":null,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"当前范围未达到 FAA 成对 epoch 质量要求"}}
            ]}}
            """)));
        Assert.Equal(0.2, preview.CurrentAt(20)?.Value);
        Assert.Null(preview.CurrentAt(21)?.Value);
        Assert.Contains("成对 epoch", preview.CurrentAt(21)?.Failure);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}

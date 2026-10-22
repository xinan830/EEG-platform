using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.BandRatio;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class BandRatioDetailTests
{
    [Fact]
    public void StaticResult_PreservesRatioEvidence()
    {
        var preview = BandRatioResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"band_ratio","band_ratio_evidence":{"numerator_band_hz":[4,8],"denominator_band_hz":[13,30],"numerator_power_v2":2.5,"denominator_power_v2":5.0}},"output":{"unit":"ratio","value":0.5},"channel":"O1","quality":{"status":"clean"},"calculation_trace":{"formula":"分子频段功率 ÷ 分母频段功率"},"actual_range":{"start_s":0,"end_s":30}},"result_contract_version":"official-algorithm-result-v1"}
            """));

        Assert.NotNull(preview);
        Assert.Equal(0.5, preview!.StaticPoint!.Value);
        Assert.Equal(4, preview.StaticPoint.Evidence.NumeratorLowHz);
        Assert.Equal(30, preview.StaticPoint.Evidence.DenominatorHighHz);
        Assert.Equal(2.5, preview.StaticPoint.Evidence.NumeratorPowerV2);
    }

    [Fact]
    public void DynamicResult_PreservesRejectedWindowAsUnavailable()
    {
        var preview = BandRatioResultPreview.Parse(Json("""
            {"metric":{"official":{"algorithm_id":"band_ratio"},"output":{"unit":"ratio","value":null},"channel":"O1","series":[{"time_s":5,"window_start_s":0,"window_end_s":5,"analysis_state":"Complete","quality":{"status":"clean"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":0.5,"unit":"ratio"}},{"time_s":6,"window_start_s":1,"window_end_s":6,"analysis_state":"Rejected","quality":{"status":"gate_failed"},"failure":{"message":"当前动态窗口未通过 PSD 质量门"},"result_contract_version":"dynamic-analysis-frame-v6","output":{"value":null,"unit":"ratio"}}],"actual_range":{"start_s":0,"end_s":30},"mode":"dynamic","result_contract_version":"dynamic-analysis-frame-v6"},"result_contract_version":"official-algorithm-result-v1"}
            """));

        Assert.NotNull(preview);
        Assert.Equal(2, preview!.DynamicPoints.Count);
        Assert.Null(preview.CurrentAt(6)!.Value);
        Assert.Contains("质量门", preview.CurrentAt(6)!.Failure);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}

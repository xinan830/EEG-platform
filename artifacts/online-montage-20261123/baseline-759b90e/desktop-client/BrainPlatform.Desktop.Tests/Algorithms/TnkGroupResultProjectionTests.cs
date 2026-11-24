using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkGroupResultProjectionTests
{
    [Fact]
    public void SnapshotChartPreservesBackendValuesUnitsAndNulls()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback"},"official":{"tnk_evidence":{"children":{
              "tnk_iapf":[{"channel":"Pz/O2","value":10.4,"unit":"Hz","quality":"clean"}],
              "tnk_faa":[{"channel":"F3/F4","value":null,"unit":"index","quality":"unavailable"}]
            }}}}}
            """);
        var cards = TnkResultChartProjection.ReadRun(Run(summary.RootElement), Definitions());
        Assert.True(cards[0].Chart.IsTimeline);
        Assert.Equal(10, cards[0].Chart.Series[0].Points[0].X);
        Assert.Equal(10.4, Assert.Single(Assert.Single(cards[0].Chart.Series).Points).Value);
        Assert.Equal("Hz", cards[0].Chart.Series[0].Unit);
        Assert.False(cards[1].Chart.HasValues);
        Assert.Null(cards[1].Chart.Series[0].Points[0].Value);
    }

    [Fact]
    public void TrendChartUsesActualEndSecondsAndRetainsMissingPoint()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback"},"official":{"tnk_evidence":{"points":[
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":10.4,"unit":"Hz","quality":"clean","actual_range":{"start_s":0,"end_s":10}}]}}}},
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":null,"unit":"Hz","quality":"unavailable","actual_range":{"start_s":1,"end_s":11}}]}}}},
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":11.2,"unit":"Hz","quality":"clean","actual_range":{"start_s":2,"end_s":12}}]}}}}
            ]}}}}
            """);
        var cards = TnkResultChartProjection.ReadRun(Run(summary.RootElement), Definitions());
        var chart = cards[0].Chart;
        Assert.True(chart.IsTimeline);
        Assert.Equal(new double[] { 10, 11, 12 }, chart.Series[0].Points.Select(point => point.X));
        Assert.Null(chart.Series[0].Points[1].Value);
        Assert.Contains("11.2", cards[0].ValueText);
    }

    [Fact]
    public void TrendChartMarksOnlyBackendNotDuePointsAsScheduledGaps()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback"},"official":{"tnk_evidence":{"points":[
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":10.4,"unit":"Hz","quality":"clean","actual_range":{"start_s":0,"end_s":30}}]}}}},
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":null,"unit":"Hz","quality":"insufficient_data","actual_range":{"start_s":1,"end_s":31},"evidence":{"extensions":{"tnk_evidence":{"provenance":{"schedule_state":"not_due"}}}}}]}}}},
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/O2","value":null,"unit":"Hz","quality":"unavailable","actual_range":{"start_s":5,"end_s":35},"failure":{"code":"TNK_CANDIDATE_INVALID","message":"invalid"}}]}}}}
            ]}}}}
            """);

        var points = Assert.Single(TnkResultChartProjection.ReadRun(Run(summary.RootElement), Definitions())[0].Chart.Series).Points;

        Assert.False(points[0].IsScheduledGap);
        Assert.True(points[1].IsScheduledGap);
        Assert.False(points[2].IsScheduledGap);
    }

    [Fact]
    public void OnDemandUsesCurrentChildEvidenceAndRejectsOtherAlgorithm()
    {
        using var evidence = JsonDocument.Parse("""
            {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[
              {"channel":"Pz-AVG/O2-AVG","value":9.7,"unit":"Hz","quality":"clean"}]}}}}
            """);
        var window = Window("tnk_biofeedback", evidence.RootElement);
        var cards = TnkResultChartProjection.ReadWindow(window, Definitions());
        Assert.True(cards[0].Chart.IsTimeline);
        Assert.Equal(10, cards[0].Chart.Series[0].Points[0].X);
        Assert.Equal(9.7, cards[0].Chart.Series[0].Points[0].Value);
        Assert.Contains("Pz-AVG/O2-AVG", cards[0].ChannelText);
        Assert.Empty(TnkResultChartProjection.ReadWindow(Window("psd", evidence.RootElement), Definitions()));
        var insufficient = window with { State = "insufficient_data", Quality = "insufficient_data", Result = null,
            Failure = new("TNK_WINDOW_INSUFFICIENT_DATA", "尚无完整窗口") };
        var empty = TnkResultChartProjection.ReadWindow(insufficient, Definitions());
        Assert.Equal(2, empty.Count);
        Assert.All(empty, card => Assert.False(card.Chart.HasValues));
        Assert.Contains("尚无完整窗口", empty[0].FailureText);
    }

    [Fact]
    public void MultipleOutputsKeepSeparateSeriesAndBackendUnits()
    {
        using var evidence = JsonDocument.Parse("""
            {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[
              {"channel":"Pz","value":null,"output_values":{"alpha":0.25,"beta":null},"unit":"ratio","quality":"partial"}]}}}}
            """);
        var cards = TnkResultChartProjection.ReadWindow(Window("tnk_biofeedback", evidence.RootElement), Definitions());
        Assert.Equal(2, cards[0].Chart.Series.Count);
        Assert.Equal("Pz · alpha", cards[0].Chart.Series[0].Name);
        Assert.Equal(0.25, cards[0].Chart.Series[0].Points[0].Value);
        Assert.Null(cards[0].Chart.Series[1].Points[0].Value);
    }

    private static DynamicWindowResponse Window(string algorithm, JsonElement evidence) => new(
        "session", "window", "request", "ready", JsonSerializer.SerializeToElement(new { algorithm_id = algorithm }),
        null, new(0, 10000, 0, 10), "partial", null,
        new(null, "ready", "algorithm_group", null, null, null, null, "partial", null, evidence.Clone(), null));

    [Fact]
    public void RejectedRbpRowsRemainFourBandGapsWithoutExtraSeries()
    {
        using var evidence = JsonDocument.Parse("""
            {"extensions":{"tnk_evidence":{"children":{"tnk_rbp":[
              {"channel":"Fz-REF","value":null,"output_values":null,"unit":"ratio","quality":"gate_failed"}]}}}}
            """);
        using var schema = JsonDocument.Parse("""
            {"children":[{"algorithm_id":"tnk_rbp","display_name_zh":"RBP","required_roles":["Fz"]}]}
            """);
        var card = Assert.Single(TnkResultChartProjection.ReadWindow(Window("tnk_biofeedback", evidence.RootElement), schema.RootElement));
        Assert.Equal(4, card.Chart.Series.Count);
        Assert.All(card.Chart.Series, series => Assert.Null(Assert.Single(series.Points).Value));
        Assert.Equal("Fz-REF · alpha", card.Chart.Series[2].Name);
    }

    [Fact]
    public void StaticProjection_UsesBackendChildValuesQualityAndFailures()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback"},"official":{"tnk_evidence":{"children":{
              "tnk_iapf":[{"channel":"Pz-AVG/Oz-AVG","value":10.4,"unit":"Hz","quality":"clean","failure":null}],
              "tnk_faa":[{"channel":"F3-AVG/F4-AVG","value":null,"unit":"index","quality":"unavailable","failure":{"code":"TNK_SEGMENT_REQUIRED","message":"需要分段范围"}}]
            }}}}}
            """);

        var projected = TnkGroupResultProjection.ReadResults(Run(summary.RootElement), Definitions());

        Assert.Equal(2, projected.Count);
        Assert.Contains("10.4 Hz", projected[0].ValueText, StringComparison.Ordinal);
        Assert.Contains("Pz-AVG/Oz-AVG", projected[0].ChannelText, StringComparison.Ordinal);
        Assert.Equal("良好", projected[0].QualityText);
        Assert.Contains("TNK_SEGMENT_REQUIRED：需要分段范围", projected[1].FailureText, StringComparison.Ordinal);
        Assert.Contains("不可用", projected[1].ValueText, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicProjection_UsesLatestBackendWindowAndNeverShowsOtherAlgorithmResults()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback","mode":"dynamic"},"official":{"tnk_evidence":{"points":[
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/Oz","value":9.8,"unit":"Hz","quality":"partial","failure":null}]}}}},
              {"extensions":{"tnk_evidence":{"children":{"tnk_iapf":[{"channel":"Pz/Oz","value":10.1,"unit":"Hz","quality":"clean","failure":null}]}}}}
            ]}}}}
            """);

        var projected = TnkGroupResultProjection.ReadResults(Run(summary.RootElement), Definitions());

        Assert.Equal(2, projected.Count);
        Assert.Contains("10.1 Hz", projected[0].ValueText, StringComparison.Ordinal);
        Assert.Equal("良好", projected[0].QualityText);

        using var otherAlgorithm = JsonDocument.Parse("""
            {"structured":{"output":{"id":"psd"},"official":{"tnk_evidence":{"children":{}}}}}
            """);
        Assert.Empty(TnkGroupResultProjection.ReadResults(Run(otherAlgorithm.RootElement), Definitions()));
    }

    [Fact]
    public void ProjectionExplainsQualityStatesWhenBackendHasNoExplicitFailureObject()
    {
        using var summary = JsonDocument.Parse("""
            {"structured":{"output":{"id":"tnk_biofeedback"},"official":{"tnk_evidence":{"children":{
              "tnk_brainbeat":[{"channel":"Fz-REF/Pz-REF","value":null,"unit":"ratio","quality":"insufficient_data","failure":null}],
              "tnk_composite_meditation":[{"channel":"Fz-REF","value":84.7,"unit":"score","quality":"degraded","failure":null}]
            }}}}}
            """);

        var projected = TnkGroupResultProjection.ReadResults(Run(summary.RootElement), DefinitionsWithQualityStates());

        Assert.Contains("数据不足", projected[0].FailureText, StringComparison.Ordinal);
        Assert.Equal("数据不足", projected[0].QualityText);
        Assert.Contains("降级结果", projected[1].FailureText, StringComparison.Ordinal);
        Assert.Equal("降级结果", projected[1].QualityText);
    }

    private static AnalysisRunResponse Run(JsonElement summary) => new(
        RunId: "run-1", RecordingId: "recording-1", AnalysisType: "official_algorithm", Status: "completed",
        ScientificVersion: "tnk-biofeedback-group-v1", RequestedRange: new(0, 10), ActualRange: new(0, 10),
        ResultSummary: summary.Clone(), AnalysisProvenance: null, Error: null,
        CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch);

    private static JsonElement Definitions() => JsonDocument.Parse("""
        {"children":[
          {"algorithm_id":"tnk_iapf","display_name_zh":"TNK 个体 Alpha 峰频率","required_roles":["Pz","Oz"]},
          {"algorithm_id":"tnk_faa","display_name_zh":"TNK 额叶 Alpha 不对称","required_roles":["F3","F4"]}
        ]}
        """).RootElement.Clone();

    private static JsonElement DefinitionsWithQualityStates() => JsonDocument.Parse("""
        {"children":[
          {"algorithm_id":"tnk_brainbeat","display_name_zh":"TNK BrainBeat","required_roles":["Fz","Pz"]},
          {"algorithm_id":"tnk_composite_meditation","display_name_zh":"TNK 综合冥想指数","required_roles":[]}
        ]}
        """).RootElement.Clone();
}

using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class ScalarOnDemandDetailTests : IDisposable
{
    public static TheoryData<string> Algorithms => new() { "band_ratio", "peak_frequency", "iapf", "theta_beta" };
    private readonly Dictionary<string, string?> previous = new();

    public ScalarOnDemandDetailTests()
    {
        foreach (var id in new[] { "band_ratio", "peak_frequency", "iapf", "theta_beta" })
        {
            var flag = Flag(id);
            previous[flag] = Environment.GetEnvironmentVariable(flag);
            Environment.SetEnvironmentVariable(flag, "1");
        }
    }

    public void Dispose()
    {
        foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
    }

    [Theory, MemberData(nameof(Algorithms))]
    public async Task Eligible_partial_value_binds_to_chart_without_a_full_run(string id)
    {
        var client = Client();
        var catalog = OnDemandDetailClient.Catalog(client, id);
        var module = ScalarWindowTestSupport.Module(new AlgorithmDetailViewModel(catalog), id);
        var changes = new List<string>();
        ((ObservableObject)module).PropertyChanged += (_, args) => changes.Add(args.PropertyName!);
        await OnDemandDetailClient.Run(catalog);
        Assert.Equal("insufficient_data", catalog.DisplayedAnalysisWindow!.State);
        Assert.True(catalog.HasDynamicPreview);
        Assert.Null(ScalarWindowTestSupport.Point(module));
        catalog.StartDynamicPreviewCommand.Execute(null);
        Assert.True(catalog.IsDynamicPreviewPlaying);
        catalog.PauseDynamicPreviewCommand.Execute(null);
        Assert.False(catalog.IsDynamicPreviewPlaying);
        catalog.DynamicTimeProgressPercent = 100 * 4d / 30;
        await catalog.CurrentWindowLoad;
        var point = ScalarWindowTestSupport.Point(module)!;
        Assert.Equal(0.5, ScalarWindowTestSupport.Value(point));
        Assert.Equal("Partial", ScalarWindowTestSupport.Property<string>(point, "State"));
        Assert.True(ScalarWindowTestSupport.Property<bool>(point, "HasValue"));
        Assert.False(ScalarWindowTestSupport.Property<bool>(point, "IsComplete"));
        Assert.True(ScalarWindowTestSupport.Property<bool>(module, "IsDynamicResult"));
        Assert.Contains("TrendPoints", changes);
        Assert.Contains("IsDynamicResult", changes);
        Assert.Null(catalog.LastRun);
        Assert.Equal(0, client.FullRunCalls);
        Assert.Equal(800, client.Requests[^1].CursorSample);
        var reopened = ScalarWindowTestSupport.Module(new AlgorithmDetailViewModel(catalog), id);
        Assert.Equal(0.5, ScalarWindowTestSupport.Value(ScalarWindowTestSupport.Point(reopened)!));
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Theory, MemberData(nameof(Algorithms))]
    public async Task Failed_science_keeps_null_and_quantitative_reason(string id)
    {
        var client = Client();
        client.Respond = request => ScalarWindowTestSupport.Response(request) with
        {
            State = "rejected", Quality = "gate_failed", Result = null,
            Failure = new DynamicWindowFailure("PSD_QUALITY_GATE_FAILED", "超幅",
                ScalarWindowTestSupport.Json(new { peak_uv = 350, threshold_uv = 200 })),
        };
        var catalog = OnDemandDetailClient.Catalog(client, id);
        var module = ScalarWindowTestSupport.Module(new AlgorithmDetailViewModel(catalog), id);
        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        var point = ScalarWindowTestSupport.Point(module)!;
        Assert.Null(ScalarWindowTestSupport.Value(point));
        Assert.False(ScalarWindowTestSupport.Property<bool>(point, "HasValue"));
        Assert.Contains("350", module.FailureDetailsText);
        Assert.Contains("200", module.FailureDetailsText);
        Assert.True(module.RejectedWindowCount > 0);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Theory, MemberData(nameof(Algorithms))]
    public async Task Channel_change_clears_history_and_reopen_keeps_cursor(string id)
    {
        var client = Client();
        var catalog = OnDemandDetailClient.Catalog(client, id);
        var module = ScalarWindowTestSupport.Module(new AlgorithmDetailViewModel(catalog), id);
        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.Equal(0.5, ScalarWindowTestSupport.Value(ScalarWindowTestSupport.Point(module)!));
        await catalog.ReleaseWindowAnalysisAsync();
        catalog.ResumeWindowAnalysis();
        await catalog.CurrentWindowLoad;
        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        Assert.Equal(2, ScalarWindowTestSupport.Points(module).Count);
        Assert.Equal(0.5, ScalarWindowTestSupport.Value(ScalarWindowTestSupport.Point(module)!));
        catalog.SelectedChannel = "F7";
        Assert.Null(ScalarWindowTestSupport.Point(module));
        Assert.Empty(ScalarWindowTestSupport.Points(module));
        await OnDemandDetailClient.Run(catalog);
        Assert.Equal("F7", client.Requests[^1].Config.GetProperty("channel").GetString());
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Theory, MemberData(nameof(Algorithms))]
    public async Task Flag_off_keeps_full_run_path(string id)
    {
        Environment.SetEnvironmentVariable(Flag(id), null);
        var client = Client();
        var catalog = OnDemandDetailClient.Catalog(client, id);
        await Assert.ThrowsAsync<NotSupportedException>(() => OnDemandDetailClient.Run(catalog));
        Assert.Equal(1, client.FullRunCalls);
        Assert.Empty(client.Requests);
        Assert.False(catalog.IsOnDemandAnalysis);
    }

    [Theory, MemberData(nameof(Algorithms))]
    public void Wrong_unit_shape_axis_and_missing_evidence_are_rejected(string id)
    {
        var response = ScalarWindowTestSupport.Response(ScalarWindowTestSupport.Request(id));
        var result = response.Result!;
        var (field, _, _) = ScalarWindowTestSupport.Schema(id);
        Assert.Throws<InvalidOperationException>(() => ScalarWindowTestSupport.Preview(id, response with
            { Result = result with { ArrayUnits = new Dictionary<string, string> { [field] = "wrong" } } }));
        Assert.Throws<InvalidOperationException>(() => ScalarWindowTestSupport.Preview(id, response with
            { Result = result with { Arrays = new Dictionary<string, JsonElement> { [field] = ScalarWindowTestSupport.Json(new[] { 1, 2 }) } } }));
        Assert.Throws<InvalidOperationException>(() => ScalarWindowTestSupport.Preview(id, response with
            { Result = result with { Axes = new Dictionary<string, JsonElement> { ["time_s"] = ScalarWindowTestSupport.Json(new[] { 7 }) } } }));
        Assert.Throws<InvalidOperationException>(() => ScalarWindowTestSupport.Preview(id, response with
            { Result = result with { Evidence = null } }));
        Assert.Throws<InvalidOperationException>(() => ScalarWindowTestSupport.Preview(id, response with
            { Result = result with { Provenance = ScalarWindowTestSupport.Json(new { analysis_state = "unknown" }) } }));
    }

    [Theory, MemberData(nameof(Algorithms))]
    public void Exact_window_is_replaced_and_history_is_bounded(string id)
    {
        object? preview = null;
        for (var index = 0; index < 300; index++)
            preview = ScalarWindowTestSupport.Preview(id,
                ScalarWindowTestSupport.Response(ScalarWindowTestSupport.Request(id, (6 + index) * 200)), preview);
        var repeated = ScalarWindowTestSupport.Response(ScalarWindowTestSupport.Request(id, 305 * 200));
        preview = ScalarWindowTestSupport.Preview(id, repeated, preview);
        var points = ((System.Collections.IEnumerable)preview.GetType().GetProperty("DynamicPoints")!.GetValue(preview)!).Cast<object>().ToArray();
        Assert.Equal(256, points.Length);
        Assert.Equal(305, ScalarWindowTestSupport.Property<double>(points[^1], "EndSeconds"));
    }

    [Fact]
    public void Module_specific_evidence_is_projected_without_recalculation()
    {
        DynamicWindowResponse Response(string id) => ScalarWindowTestSupport.Response(ScalarWindowTestSupport.Request(id));
        var band = BandRatioResultPreview.FromWindow(Response("band_ratio"), "F3").DynamicPoints.Single();
        Assert.Equal(8, band.Evidence.NumeratorLowHz);
        Assert.Equal(25, band.Evidence.DenominatorHighHz);
        Assert.Equal(2e-12, band.Evidence.NumeratorPowerV2);
        Assert.Equal("backend formula", band.Evidence.Formula);
        Assert.Equal(3e-12, PeakFrequencyResultPreview.FromWindow(Response("peak_frequency"), "F3").DynamicPoints.Single().PeakPowerV2PerHz);
        var iapf = IapfResultPreview.FromWindow(Response("iapf"), "F3").DynamicPoints.Single();
        Assert.Equal("peak", iapf.Source);
        Assert.Equal(0.95, iapf.ModelR2);
        var theta = ThetaBetaResultPreview.FromWindow(Response("theta_beta"), "F3").DynamicPoints.Single();
        Assert.Equal(10, theta.Evidence.IapfHz);
        Assert.Equal(11, theta.Evidence.BetaLowHz);
        Assert.Equal(4, theta.Evidence.BetaPowerUv2);
        Assert.Equal("backend formula", theta.Evidence.Formula);
    }

    private static OnDemandDetailClient Client() => new() { Respond = ScalarWindowTestSupport.Response };
    private static string Flag(string id) => "BRAIN_WPF_ON_DEMAND_" + id.ToUpperInvariant();
}

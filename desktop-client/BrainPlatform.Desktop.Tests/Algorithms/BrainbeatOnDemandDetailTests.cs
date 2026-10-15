using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class BrainbeatOnDemandDetailTests : IDisposable
{
    private readonly string? previous;

    public BrainbeatOnDemandDetailTests()
    {
        previous = Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_BRAINBEAT");
        Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_BRAINBEAT", "1");
    }

    public void Dispose() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_BRAINBEAT", previous);

    [Fact]
    public async Task Ready_window_binds_brainbeat_value_and_pair_evidence()
    {
        var client = new OnDemandDetailClient { Respond = Response };
        var catalog = OnDemandDetailClient.Catalog(client, "brainbeat", primaryChannel: "Fz", secondaryChannel: "Pz");
        var detail = new AlgorithmDetailViewModel(catalog).Brainbeat;

        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;

        Assert.Equal(0.75, detail.CurrentPoint?.Value);
        Assert.Equal("Fz / Pz", detail.ChannelText);
        Assert.Equal("良好", detail.QualityText);
        Assert.Single(detail.TrendPoints);
    }

    [Fact]
    public void Ready_window_rejects_missing_independent_window_provenance()
    {
        var response = Response(new DynamicWindowRequest("recording", "brainbeat", null, null,
            JsonSerializer.SerializeToElement(new { }), 1000, RequestId: "request"));
        var invalid = response with
        {
            Result = response.Result! with { Provenance = JsonSerializer.SerializeToElement(new { paired_sources = true }) },
        };

        Assert.Throws<InvalidOperationException>(() => BrainbeatResultPreview.FromWindow(invalid, "Fz / Pz"));
    }

    [Fact]
    public void Ready_window_rejects_unverified_source_roles_or_iapf_dependency()
    {
        var response = Response(new DynamicWindowRequest("recording", "brainbeat", null, null,
            JsonSerializer.SerializeToElement(new { }), 1000, RequestId: "request"));
        var invalid = response with
        {
            Result = response.Result! with
            {
                Provenance = JsonSerializer.SerializeToElement(new
                {
                    preprocessing_scope = "whole_recording_before_window_slice",
                    paired_sources = true,
                    source_channels = new { fz = "F7", pz = "Pz" },
                    independent_windows = true,
                    cross_window_ema = false,
                    iapf_dependency = false,
                    analysis_state = "Complete",
                }),
            },
        };

        Assert.Throws<InvalidOperationException>(() => BrainbeatResultPreview.FromWindow(invalid, "Fz / Pz"));
    }

    private static DynamicWindowResponse Response(DynamicWindowRequest request)
    {
        var end = request.CursorSample / 200d;
        var ready = end >= 5;
        var result = new DynamicWindowResultResponse(
            null, ready ? "ready" : "insufficient_data", ready ? "scalar_series" : null,
            new Dictionary<string, JsonElement> { ["time_s"] = JsonSerializer.SerializeToElement(new[] { end }) },
            new Dictionary<string, string> { ["time_s"] = "s" },
            new Dictionary<string, JsonElement> { ["brainbeat"] = JsonSerializer.SerializeToElement(new[] { 0.75 }) },
            new Dictionary<string, string> { ["brainbeat"] = "ratio" }, "clean", null,
            JsonSerializer.SerializeToElement(new
            {
                extensions = new { brainbeat_evidence = new { fz_channel = "Fz", pz_channel = "Pz", iapf_hz = 10.0 } },
                calculation_trace = new { formula = "Fz 相对 Theta ÷ Pz 相对 Alpha" },
            }),
            JsonSerializer.SerializeToElement(new
            {
                preprocessing_scope = "whole_recording_before_window_slice",
                analysis_state = "Complete",
                paired_sources = true,
                source_channels = new { fz = "Fz", pz = "Pz" },
                independent_windows = true,
                cross_window_ema = false,
                iapf_dependency = true,
            }));
        return new DynamicWindowResponse(
            "session", "window", request.RequestId ?? "request", ready ? "ready" : "insufficient_data",
            JsonSerializer.SerializeToElement(new { recording_id = "recording" }), null,
            new DynamicWindowRange(Math.Max(0, request.CursorSample - 1000), request.CursorSample,
                ready ? Math.Max(0, end - 5) : end, ready ? end : end + 0.005
                ), ready ? "clean" : "insufficient_data",
            ready ? null : new DynamicWindowFailure("BRAINBEAT_WINDOW_INSUFFICIENT_DATA", "数据不足"),
            ready ? result : null, "subscription");
    }
}

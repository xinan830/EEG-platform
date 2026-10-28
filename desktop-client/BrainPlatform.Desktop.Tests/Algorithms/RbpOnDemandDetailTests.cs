using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Rbp;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("OnDemandWindowFeature")]
public sealed class RbpOnDemandDetailTests : IDisposable
{
    private readonly string? previous = Environment.GetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_RBP");
    public RbpOnDemandDetailTests() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_RBP", "1");
    public void Dispose() => Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_RBP", previous);

    [Fact]
    public async Task First_window_timeline_then_five_bands_without_full_run()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Rbp;
        await OnDemandDetailClient.Run(catalog);
        Assert.True(catalog.HasDynamicPreview);
        Assert.True(catalog.StartDynamicPreviewCommand.CanExecute(null));
        Assert.False(detail.HasResult);
        catalog.DynamicTimeProgressPercent = 100 * 6d / 30;
        await catalog.CurrentWindowLoad;
        Assert.Equal(5, detail.Bands.Count);
        Assert.Equal(0.6, detail.Bands[2].Share);
        Assert.Contains("部分完成", detail.WindowText);
        Assert.Null(catalog.LastRun);
        Assert.Equal(0, client.FullRunCalls);
        Assert.True(new AlgorithmDetailViewModel(catalog).Rbp.HasResult);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Visited_channel_history_appends_without_replacing_chart_data_collection()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Rbp;
        await OnDemandDetailClient.Run(catalog);

        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        var history = Assert.Single(detail.ChannelHistories);
        var plottedWindows = history.Windows;
        Assert.Single(plottedWindows);

        catalog.DynamicTimeProgressPercent = 40;
        await catalog.CurrentWindowLoad;

        Assert.Same(plottedWindows, detail.ChannelHistories[0].Windows);
        Assert.Equal(2, plottedWindows.Count);
        Assert.Equal(12, plottedWindows[^1].EndSeconds);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Failure_retains_quantities_and_no_fabricated_zero()
    {
        var client = new OnDemandDetailClient { Respond = request => OnDemandDetailClient.RbpResponse(request) with
        {
            State = "rejected", Quality = "gate_failed", Result = null,
            Failure = new DynamicWindowFailure("RBP_QUALITY_GATE_FAILED", "超幅",
                JsonSerializer.SerializeToElement(new { peak_uv = 350, threshold_uv = 200 })),
        }};
        var catalog = OnDemandDetailClient.Catalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Rbp;
        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 100 * 11d / 30;
        await catalog.CurrentWindowLoad;
        Assert.All(detail.Bands, band => Assert.Null(band.Share));
        Assert.Contains("350", detail.FailureDetailsText);
        Assert.Contains("200", detail.FailureDetailsText);
        Assert.True(detail.RejectedWindowCount > 0);
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Channel_change_clears_history_and_close_reopen_preserves_cursor()
    {
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        var detail = new AlgorithmDetailViewModel(catalog).Rbp;
        await OnDemandDetailClient.Run(catalog);
        catalog.DynamicTimeProgressPercent = 20;
        await catalog.CurrentWindowLoad;
        Assert.True(detail.HasResult);
        await catalog.ReleaseWindowAnalysisAsync();
        catalog.ResumeWindowAnalysis();
        await catalog.CurrentWindowLoad;
        Assert.Equal(6, catalog.DynamicPreviewCursorSeconds);
        catalog.SelectedChannel = "F7";
        Assert.False(detail.HasResult);
        Assert.Empty(detail.Trend);
        await OnDemandDetailClient.Run(catalog);
        Assert.Equal("F7", client.Requests[^1].Config.GetProperty("channel").GetString());
        await catalog.ReleaseWindowAnalysisAsync();
    }

    [Fact]
    public async Task Flag_off_keeps_legacy_path()
    {
        Environment.SetEnvironmentVariable("BRAIN_WPF_ON_DEMAND_RBP", "0");
        var client = new OnDemandDetailClient();
        var catalog = OnDemandDetailClient.Catalog(client);
        await Assert.ThrowsAsync<NotSupportedException>(() => OnDemandDetailClient.Run(catalog));
        Assert.Equal(1, client.FullRunCalls);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void Invalid_unit_or_shape_is_rejected_and_visited_history_is_bounded()
    {
        using var config = JsonDocument.Parse("{\"channel\":\"F3\"}");
        var response = OnDemandDetailClient.RbpResponse(new DynamicWindowRequest("recording", "rbp", null, null,
            config.RootElement.Clone(), 1200, RequestId: "request"));
        var result = response.Result!;
        var wrongUnits = new Dictionary<string, string>(result.ArrayUnits!) { ["alpha"] = "Hz" };
        Assert.Throws<InvalidOperationException>(() => RbpResultPreview.FromWindow(response with
            { Result = result with { ArrayUnits = wrongUnits } }));
        var wrongArrays = new Dictionary<string, JsonElement>(result.Arrays!) { ["alpha"] = JsonSerializer.SerializeToElement(new[] { 0.2, 0.4 }) };
        Assert.Throws<InvalidOperationException>(() => RbpResultPreview.FromWindow(response with
            { Result = result with { Arrays = wrongArrays } }));
        RbpResultPreview? preview = null;
        for (var index = 0; index < 300; index++)
            preview = RbpResultPreview.FromWindow(response with { Range = new DynamicWindowRange(0, 1200, -index, 6) }, preview);
        Assert.Equal(256, preview!.Windows.Length);
    }
}

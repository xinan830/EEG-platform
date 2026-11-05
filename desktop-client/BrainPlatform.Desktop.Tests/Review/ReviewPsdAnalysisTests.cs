using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Tests.Algorithms;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewPsdAnalysisTests
{
    [Fact]
    public void Topomap_options_expose_the_five_canonical_band_labels()
    {
        var options = new PsdReviewAnalysisViewModel().TopomapBandOptions;

        Assert.Equal(new[] { "delta", "theta", "alpha", "beta", "gamma" }, options.Select(item => item.Id));
        Assert.Equal("Gamma · 30–50 Hz", options[^1].Label);
    }

    [Fact]
    public void Parameters_capture_ordered_channels_from_the_montage_snapshot()
    {
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 200, 40,
            ["F3-REF", "F7-REF"], "sha");
        var algorithm = Algorithm();
        var montage = new HistoricalMontageDescriptor("ant-ref", "r1", "hash1", "ANT REF", true, true, null,
            [new HistoricalMontageOutput("F3-REF", "F3", "F3", "V", true, null),
             new HistoricalMontageOutput("F7-REF", "F7", "F7", "V", true, null)]);
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(recording, algorithm, montage);

        var config = parameters.BuildConfig(40, 200, montage);

        Assert.Equal("psd", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("fixture-psd-v1", config.GetProperty("scientific_version").GetString());
        Assert.Equal(new[] { "F3-REF", "F7-REF" },
            config.GetProperty("channels").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("dynamic", config.GetProperty("mode").GetString());
        Assert.Equal(10, config.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, config.GetProperty("refresh_step_s").GetDouble());
        Assert.Equal(1, config.GetProperty("low_hz").GetDouble());
        Assert.Equal(30, config.GetProperty("high_hz").GetDouble());
        Assert.Equal(50, config.GetProperty("notch_hz").GetDouble());
        Assert.Equal("ant-ref", config.GetProperty("analysis_montage_id").GetString());
        var outputs = config.GetProperty("analysis_montage_outputs").EnumerateArray().ToArray();
        Assert.Equal(new[] { "F3-REF", "F7-REF" }, outputs.Select(item => item.GetProperty("output").GetString()));
        Assert.Equal(new[] { "F3", "F7" }, outputs.Select(item => item.GetProperty("role").GetString()));
        Assert.All(outputs, output => Assert.Equal("V", output.GetProperty("unit").GetString()));
    }

    [Fact]
    public async Task Presenter_projects_backend_psd_and_clears_it_for_rejected_windows()
    {
        var client = new OnDemandDetailClient { Respond = OnDemandDetailClient.PsdResponse };
        var adapter = new ReviewWindowAnalysisAdapter(client, "psd", "PSD", true);
        var model = new PsdReviewAnalysisViewModel();
        var request = Request();

        model.Apply(await adapter.LoadOnDemandAsync("recording", request, 2000, CancellationToken.None));

        Assert.True(model.HasResult);
        Assert.Equal([1e12, 2e12, 1e12], model.Points.Cast<PsdPreviewPoint>().Select(point => point.Value));
        Assert.Equal("µV²/Hz", model.ValueUnit);
        Assert.Empty(model.StatusText);
        Assert.Equal("5–10 s", model.CurrentRangeText);

        client.Respond = pending => OnDemandDetailClient.PsdResponse(pending) with
        {
            State = "rejected", Quality = "rejected", Result = null,
            Failure = new DynamicWindowFailure("PSD_QUALITY_REJECTED", "质量规则拒绝", null),
        };
        model.Apply(await adapter.LoadOnDemandAsync("recording", request, 2200, CancellationToken.None));

        Assert.False(model.HasResult);
        Assert.Empty(model.Points);
        Assert.Contains("PSD_QUALITY_REJECTED", model.StatusText);
    }

    [Fact]
    public void Current_range_tracks_the_backend_topomap_window_and_is_unavailable_when_cleared()
    {
        var model = new PsdReviewAnalysisViewModel();
        model.ApplyTopomap(new TopomapResponse("window", "recording", 10, 20, "ready", "v1", "build",
            "mne", "mri", "m", [], null, null));
        model.SelectedView = "脑电地形图";

        Assert.Equal("10–20 s", model.CurrentRangeText);

        model.ClearTopomap();

        Assert.Equal("分析范围不可用", model.CurrentRangeText);
    }

    [Theory]
    [InlineData("axis-unit")]
    [InlineData("array-unit")]
    [InlineData("shape")]
    public async Task Presenter_rejects_malformed_backend_psd(string defect)
    {
        var client = new OnDemandDetailClient
        {
            Respond = request =>
            {
                var response = OnDemandDetailClient.PsdResponse(request);
                var result = response.Result!;
                return defect switch
                {
                    "axis-unit" => response with { Result = result with
                        { AxisUnits = new Dictionary<string, string> { ["frequency_hz"] = "s" } } },
                    "array-unit" => response with { Result = result with
                        { ArrayUnits = new Dictionary<string, string> { ["psd"] = "ratio" } } },
                    _ => response with { Result = result with
                        { Arrays = new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(new[] { 1d }) } } },
                };
            },
        };
        var adapter = new ReviewWindowAnalysisAdapter(client, "psd", "PSD", true);
        var model = new PsdReviewAnalysisViewModel();

        model.Apply(await adapter.LoadOnDemandAsync("recording", Request(), 2000, CancellationToken.None));

        Assert.False(model.HasResult);
        Assert.Empty(model.Points);
        Assert.Contains("PSD 结果无法展示", model.StatusText);
    }

    private static AnalysisRunRequest Request() => new("recording", "official_algorithm",
        JsonSerializer.SerializeToElement(new { algorithm_id = "psd", channels = new[] { "F3" } }));

    internal static IReadOnlyList<AlgorithmParameter> CatalogParameters() =>
    [
        new("low_hz", "高通截止频率", "number", true, JsonSerializer.SerializeToElement(1d), "Hz", 0, null, .1, true, "", [], ""),
        new("high_hz", "低通截止频率", "number", true, JsonSerializer.SerializeToElement(30d), "Hz", 0, null, .1, true, "", [], ""),
        new("notch_hz", "陷波频率", "enum", false, JsonSerializer.SerializeToElement(50d), "Hz", null, null, null, true, "", [], ""),
    ];

    private static AlgorithmCatalogItem Algorithm() => new("official", "psd", "fixture-psd-v1", "功率谱密度",
        "PSD", "", CatalogParameters(), ["dynamic"], default, new DynamicAnalysisPolicy(4, [5, 10, 20, 30], 10, 1, true),
        "available", true, "psd-definition", "1", "psd-runtime-v1");
}

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task Review_psd_uses_shared_cursor_registration_and_on_demand_lifecycle()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F7"]), [], []);
        var provider = new PanelProvider("psd");
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/psd-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);

        await vm.OpenAnalysisAsync("psd");

        var config = Assert.Single(provider.Configurations);
        Assert.Equal("psd", config.GetProperty("algorithm_id").GetString());
        Assert.Equal(new[] { "F3", "F7" },
            config.GetProperty("channels").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(10, vm.PositionSeconds);
        Assert.Equal(10, vm.AnalysisPanelStartSeconds);
        Assert.True(vm.IsPsdAnalysisPanelOpen);
        Assert.True(vm.IsAnalysisAttached);
    }

    [Fact]
    public async Task Closing_psd_cancels_its_window_without_moving_review_cursor()
    {
        var configuration = Configuration();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3", "F7"]), [], []);
        var provider = new PanelProvider("psd", deferWindow: true);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100), catalog,
            recordingDirectory: "_temp/psd-review-fixture", analysisCoordinators: [new ReviewAnalysisCoordinator(provider)]);
        await vm.InitializeAsync();
        await vm.SeekAsync(10);
        var open = vm.OpenAnalysisAsync("psd");
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var close = vm.CloseAnalysisAsync();
        Assert.True(provider.PendingToken.IsCancellationRequested);
        provider.Release.TrySetResult();
        await Task.WhenAll(open, close).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(vm.IsPsdAnalysisPanelOpen);
        Assert.Equal(10, vm.PositionSeconds);
        Assert.False(vm.PsdAnalysis.HasResult);
    }
}

using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Tests.Algorithms;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class StftReviewPresentationTests
{
    [Fact]
    public async Task Adapter_and_presenter_preserve_units_and_absolute_time_once()
    {
        var preview = await Ready();
        var model = new StftReviewAnalysisViewModel();
        model.Apply(preview);

        Assert.Equal([7d, 8d], model.Preview!.TimesSeconds);
        Assert.Equal([10d, 11d], model.Preview.FrequenciesHz);
        Assert.Equal(1d, model.Preview.PowerDb[0][0]);
        Assert.Equal("5–10 s", model.CurrentWindowRangeText);
        Assert.Equal("time_frequency", preview.Output!.Value.GetProperty("kind").GetString());
        Assert.Equal("s", preview.AxisMetadata!["time_center_s"].GetProperty("unit").GetString());
        Assert.Equal("V^2/Hz", preview.ArrayMetadata!["power_linear"].GetProperty("unit").GetString());
        Assert.Equal(5, model.Preview.RequestedRange.StartSeconds);
    }

    [Fact]
    public async Task Current_range_tracks_backend_windows_and_is_unavailable_without_backend_bounds()
    {
        var ready = await Ready();
        var model = new StftReviewAnalysisViewModel();

        model.Apply(ready);
        Assert.Equal("5–10 s", model.CurrentWindowRangeText);

        model.Apply(ready with { WindowStartSeconds = 6, WindowEndSeconds = 11 });
        Assert.Equal("6–11 s", model.CurrentWindowRangeText);

        model.Apply(ready with { WindowStartSeconds = null, WindowEndSeconds = null });
        Assert.Equal("分析范围不可用", model.CurrentWindowRangeText);
    }

    [Theory]
    [InlineData("units")]
    [InlineData("metadata")]
    [InlineData("quality")]
    [InlineData("shape")]
    [InlineData("time")]
    public async Task Invalid_result_clears_previous_image_and_reports_reason(string defect)
    {
        var ready = await Ready();
        var model = new StftReviewAnalysisViewModel();
        model.Apply(ready);
        Assert.True(model.HasResult);
        var broken = defect switch
        {
            "units" => ready with { ArrayMetadata = ready.ArrayMetadata!.ToDictionary(p => p.Key,
                p => p.Key == "power_db" ? Json(new { unit = "unknown" }) : p.Value) },
            "metadata" => ready with { AxisMetadata = new Dictionary<string, JsonElement>
                { ["time_center_s"] = Json(1), ["frequency_hz"] = Json(new { unit = "Hz" }) } },
            "quality" => ready with { Quality = Json(new { status = "rejected", state = "ready" }) },
            "shape" => ready with { Arrays = ready.Arrays!.ToDictionary(p => p.Key,
                p => p.Key == "power_db" ? Json(new[] { new[] { 1d } }) : p.Value) },
            _ => ready with { WindowEndSeconds = 6 },
        };
        model.Apply(broken);
        Assert.False(model.HasResult);
        Assert.Contains("无法展示", model.StatusText);
        Assert.Throws<InvalidOperationException>(() => StftPreview.FromReviewWindow(broken));
    }

    [Fact]
    public async Task Rejection_keeps_backend_failure_and_never_keeps_old_power()
    {
        var client = new OnDemandDetailClient
        {
            Respond = request => Response(request) with
            {
                State = "rejected", Quality = "rejected", Result = null,
                Failure = new DynamicWindowFailure("STFT_ARTIFACT", "幅度超限", Json(new { observed_uv = 500 })),
            },
        };
        var adapter = new ReviewWindowAnalysisAdapter(client, "stft", "STFT", true);
        var model = new StftReviewAnalysisViewModel();
        model.Apply(await Ready());
        model.Apply(await adapter.LoadOnDemandAsync("recording", Request(), 2000, CancellationToken.None));
        Assert.Null(model.Preview);
        Assert.Contains("STFT_ARTIFACT", model.StatusText);
    }

    [Fact]
    public void Parameters_preserve_selected_channel_and_catalogue_policy_on_refresh()
    {
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 200, 40, ["F3", "F7"], "sha");
        var algorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v3-bounded", "STFT", "", "",
            [], ["dynamic"], default, new DynamicAnalysisPolicy(4, [5, 10], 5, 1, true),
            "available", true, "stft-definition", "1.1.0", "stft-runtime-v6-bounded");
        var parameters = new ReviewStftParameters();
        parameters.Initialize(recording, algorithm, null);
        parameters.Channel = "F7";
        parameters.Initialize(recording, algorithm, null);
        var config = parameters.BuildConfig(40, 200, null);
        Assert.Equal("F7", config.GetProperty("channel").GetString());
        Assert.Equal("spectrogram-v3-bounded", config.GetProperty("scientific_version").GetString());
        Assert.Equal(5, config.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, config.GetProperty("refresh_step_s").GetDouble());
        Assert.Equal(40, config.GetProperty("time").GetProperty("end_s").GetDouble());
        Assert.Equal(50, config.GetProperty("high_hz").GetDouble());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("analysis_montage_outputs").ValueKind);
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(40, 100, null));
        parameters.Channel = "missing";
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(40, 200, null));
    }

    internal static async Task<WindowPreviewResponse> Ready()
    {
        var adapter = new ReviewWindowAnalysisAdapter(new OnDemandDetailClient { Respond = Response }, "stft", "STFT", true);
        return await adapter.LoadOnDemandAsync("recording", Request(), 2000, CancellationToken.None);
    }

    [Fact]
    public void Montage_change_preserves_role_and_serializes_only_the_selected_output()
    {
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 200, 40, ["F3", "F7"], "sha");
        var algorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v3-bounded", "STFT", "", "",
            [], ["dynamic"], default, new DynamicAnalysisPolicy(4, [5], 5, 1, true),
            "available", true, "stft-definition", "1.1.0", "stft-runtime-v6-bounded");
        var original = new HistoricalMontageDescriptor("old", "r1", "hash1", "original", true, true, null,
            [new HistoricalMontageOutput("F7-REF", "F7", "F7", "V", true, null)]);
        var replacement = new HistoricalMontageDescriptor("new", "r2", "hash2", "replacement", true, true, null,
            [new HistoricalMontageOutput("F3-AVG", "F3", "F3", "V", true, null),
             new HistoricalMontageOutput("F4-AVG", "F4", "F4", "V", false, "missing"),
             new HistoricalMontageOutput("F7-AVG", "F7", "F7", "V", true, null)]);
        var parameters = new ReviewStftParameters();
        parameters.Initialize(recording, algorithm, original);
        parameters.RefreshInputOptions(recording, original, replacement);

        var config = parameters.BuildConfig(40, 200, replacement);

        Assert.Equal(["F3-AVG", "F7-AVG"], parameters.Channels);
        Assert.Equal("F7-AVG", config.GetProperty("channel").GetString());
        Assert.Equal("new", config.GetProperty("analysis_montage_id").GetString());
        Assert.Equal("r2", config.GetProperty("analysis_montage_revision").GetString());
        Assert.Equal("hash2", config.GetProperty("analysis_montage_fingerprint").GetString());
        var output = Assert.Single(config.GetProperty("analysis_montage_outputs").EnumerateArray());
        Assert.Equal("F7-AVG", output.GetProperty("output").GetString());
        Assert.Equal("F7", output.GetProperty("role").GetString());
        Assert.Equal("V", output.GetProperty("unit").GetString());
    }

    [Fact]
    public void Initial_and_changed_channel_mapping_matches_exactly_the_single_stft_input()
    {
        var recording = new RegisteredRecording("recording", "fixture", ".wpf", "now", 200, 40, ["Fp1", "F7"], "sha");
        var algorithm = new AlgorithmCatalogItem("official", "stft", "spectrogram-v3-bounded", "STFT", "", "",
            [], ["dynamic"], default, new DynamicAnalysisPolicy(4, [5], 5, 1, true),
            "available", true, "stft-definition", "1.1.0", "stft-runtime-v6-bounded");
        var montage = new HistoricalMontageDescriptor("ant-ref", "r1", "hash1", "ANT REF", true, true, null,
            [new HistoricalMontageOutput("Fp1-REF", "Fp1", "Fp1", "V", true, null),
             new HistoricalMontageOutput("F4-REF", "F4", "F4", "V", false, "missing"),
             new HistoricalMontageOutput("F7-REF", "F7", "F7", "V", true, null)]);
        var parameters = new ReviewStftParameters();
        parameters.Initialize(recording, algorithm, montage);
        Assert.Equal("Fp1-REF", parameters.Channel);

        foreach (var (channel, role) in new[] { ("Fp1-REF", "Fp1"), ("F7-REF", "F7") })
        {
            parameters.Channel = channel;
            var config = parameters.BuildConfig(40, 200, montage);
            var output = Assert.Single(config.GetProperty("analysis_montage_outputs").EnumerateArray());
            Assert.Equal(config.GetProperty("channel").GetString(), output.GetProperty("output").GetString());
            Assert.Equal(channel, output.GetProperty("output").GetString());
            Assert.Equal(role, output.GetProperty("role").GetString());
            Assert.Equal("V", output.GetProperty("unit").GetString());
            Assert.Equal("ant-ref", config.GetProperty("analysis_montage_id").GetString());
            Assert.Equal("r1", config.GetProperty("analysis_montage_revision").GetString());
            Assert.Equal("hash1", config.GetProperty("analysis_montage_fingerprint").GetString());
            Assert.Equal(["Fp1-REF", "F7-REF"], parameters.Channels);
        }
    }

    private static AnalysisRunRequest Request() => new("recording", "official_algorithm", Json(new { algorithm_id = "stft", channel = "F3" }));
    private static DynamicWindowResponse Response(DynamicWindowRequest request)
    {
        var response = OnDemandDetailClient.StftResponse(request);
        return response with { Result = response.Result! with { Evidence = Json(new { threshold = 200 }) } };
    }
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}

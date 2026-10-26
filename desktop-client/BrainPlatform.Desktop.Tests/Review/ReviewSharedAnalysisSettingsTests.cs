using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewSharedAnalysisSettingsTests
{
    [Fact]
    public void Analysis_choices_and_request_preserve_acquisition_montage_outputs()
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording() with { Channels = ["F7-REF", "F3-REF"] }, Algorithm());
        Assert.Equal(new[] { "F7-REF", "F3-REF" }, parameters.Channels);
        Assert.Equal("F7-REF", parameters.Channel);
        parameters.Channel = "F3-REF";
        var json = parameters.BuildConfig(100, 500, new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5));
        Assert.Equal("F3-REF", json.GetProperty("channel").GetString());
        parameters.Channel = "F3";
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500,
            new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(60)]
    public void Request_captures_shared_filters_and_window(double notchHz)
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording(), Algorithm());
        parameters.Channel = "F4";
        var snapshot = new ReviewAnalysisSettingsSnapshot(0.5, 30, notchHz, 10);

        var json = parameters.BuildConfig(100, 500, snapshot);

        Assert.Equal(0.5, json.GetProperty("low_hz").GetDouble());
        Assert.Equal(30, json.GetProperty("high_hz").GetDouble());
        Assert.Equal(10, json.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal("F4", json.GetProperty("channel").GetString());
        Assert.Equal("dynamic", json.GetProperty("mode").GetString());
        Assert.Equal(100, json.GetProperty("time").GetProperty("end_s").GetDouble());
        if (notchHz == 0) Assert.Equal(JsonValueKind.Null, json.GetProperty("notch_hz").ValueKind);
        else Assert.Equal(notchHz, json.GetProperty("notch_hz").GetDouble());
    }

    [Fact]
    public void Psd_request_config_has_no_waveform_display_montage_or_scale_fields()
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording(), Algorithm());
        var config = parameters.BuildConfig(100, 500,
            new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5));

        Assert.False(config.TryGetProperty("viewing_montage", out _));
        Assert.False(config.TryGetProperty("sensitivity", out _));
        Assert.False(config.TryGetProperty("paper_speed", out _));
        Assert.Equal("F3", config.GetProperty("channel").GetString());
    }

    [Fact]
    public void Psd_scientific_channel_and_filter_changes_change_the_request_config()
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording() with { Channels = ["F3", "F4"] }, Algorithm());
        var baseline = parameters.BuildConfig(100, 500, new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5));

        parameters.Channel = "F4";
        var channelChanged = parameters.BuildConfig(100, 500, new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5));
        var filterChanged = parameters.BuildConfig(100, 500, new ReviewAnalysisSettingsSnapshot(1, 40, 50, 5));

        Assert.NotEqual(baseline.GetRawText(), channelChanged.GetRawText());
        Assert.Equal("F4", channelChanged.GetProperty("channel").GetString());
        Assert.NotEqual(channelChanged.GetRawText(), filterChanged.GetRawText());
        Assert.Equal(40, filterChanged.GetProperty("high_hz").GetDouble());
    }

    [Fact]
    public void Switching_review_montage_maps_psd_channel_role_and_binds_snapshot()
    {
        var refMontage = Montage("ref", "f-ref", "Fp1-REF", "Fp2-REF");
        var averageMontage = Montage("average", "f-average", "Fp1-AVG", "Fp2-AVG");
        var recording = Recording() with
        {
            MontageCatalogue = [refMontage, averageMontage],
        };
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(recording, Algorithm(), refMontage);
        parameters.Channel = "Fp2-REF";

        parameters.SelectMontage(recording, averageMontage);
        var config = parameters.BuildConfig(100, 500,
            new ReviewAnalysisSettingsSnapshot(1, 30, 50, 5));

        Assert.Equal("Fp2-AVG", parameters.Channel);
        Assert.Equal("average", config.GetProperty("analysis_montage_id").GetString());
        Assert.Equal("f-average", config.GetProperty("analysis_montage_fingerprint").GetString());
        Assert.Equal("Fp2-AVG", config.GetProperty("channel").GetString());
        Assert.Equal("Fp2", config.GetProperty("analysis_montage_outputs")[0].GetProperty("role").GetString());
    }

    [Fact]
    public void Window_choices_and_fallback_are_from_backend_policy()
    {
        var settings = new ReviewAnalysisSettings();
        settings.Initialize(new DynamicAnalysisPolicy(8, [8, 12], 8, 2, false));
        Assert.Equal([8d, 12d], settings.WindowOptions);
        Assert.Equal(8, settings.WindowSeconds);
        settings.WindowSeconds = 12;
        Assert.Equal(12, settings.WindowSeconds);
        settings.WindowSeconds = 5;
        Assert.Equal(12, settings.WindowSeconds);
    }

    [Fact]
    public void Unsupported_window_cannot_be_submitted()
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording(), Algorithm());
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500,
            new ReviewAnalysisSettingsSnapshot(1, 30, 50, 7)));
    }

    [Theory]
    [InlineData(40, 30, 50)]
    [InlineData(1, 250, 50)]
    [InlineData(1, 30, 55)]
    public void Invalid_shared_filter_values_are_rejected(double highPass, double lowPass, double notch)
    {
        var parameters = new ReviewPsdParameters();
        parameters.Initialize(Recording(), Algorithm());
        Assert.Throws<InvalidOperationException>(() => parameters.BuildConfig(100, 500,
            new ReviewAnalysisSettingsSnapshot(highPass, lowPass, notch, 5)));
    }

    internal static RegisteredRecording Recording() => new("backend-recording", "test", "raw",
        "1970-01-01T00:00:00Z", 500, 100, ["F3", "F4"], "hash");

    internal static AlgorithmCatalogItem Algorithm() => new("official", "psd", "v1", "功率谱密度",
        "PSD", "", [], ["dynamic"], JsonSerializer.SerializeToElement(new { }),
        new DynamicAnalysisPolicy(5, [5, 10], 5, 1, false), "available", true, null, null, "test");

    private static HistoricalMontageDescriptor Montage(string id, string fingerprint, params string[] channels) =>
        new(id, "acquisition-montage-v1", fingerprint, id, id == "ref", true, null,
            channels.Select(channel => new HistoricalMontageOutput(channel, channel,
                channel[..channel.IndexOf('-')], "V", true, null)).ToArray(), id);
}

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData("filter")]
    [InlineData("window")]
    [InlineData("channel")]
    public async Task Changed_configuration_cannot_attach_late_result_or_reset_cursor(string changedSetting)
    {
        var configuration = Configuration();
        var provider = new PendingReviewProvider();
        var catalog = new RecordingMontageCatalogResult(null, AcquisitionMontageStatus.MissingSnapshot,
            new RawSignalViewDefinition(["F3"]), [], []);
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration)), catalog,
            recordingDirectory: "test-directory", analysisCoordinator: new ReviewAnalysisCoordinator(provider));
        await vm.InitializeAsync();
        await vm.SeekAsync(17);
        vm.TogglePlayback();
        var pending = vm.PrepareDefaultPsdAsync();
        await provider.Started.Task;
        if (changedSetting == "filter") vm.LowPassHz = 40;
        if (changedSetting == "window") vm.AnalysisSettings.WindowSeconds = 10;
        if (changedSetting == "channel") vm.PsdParameters.Channel = "F4";
        provider.Completed.SetResult(new AnalysisRunResponse("run", "backend-recording", "official_algorithm",
            "completed", "v1", null, null, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        await pending;

        Assert.Null(vm.CurrentAnalysisWindow);
        Assert.False(vm.IsAnalysisAttached);
        Assert.Equal(17, vm.PositionSeconds);
        Assert.True(vm.IsPlaying);
        Assert.Contains("参数已改变", vm.PsdPreparationText);
        Assert.Equal(30, provider.Request!.Config.GetProperty("high_hz").GetDouble());
        Assert.Equal(5, provider.Request.Config.GetProperty("dynamic_window_s").GetDouble());
    }

    private sealed class PendingReviewProvider : IReviewAlgorithmAdapter
    {
        public string AlgorithmId => "psd";
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource<AnalysisRunResponse> Completed { get; } = new();
        public AnalysisRunRequest? Request { get; private set; }
        public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken,
            IReadOnlyList<BrainPlatform.Desktop.Modules.Montages.Domain.MontageProfile>? montageProfiles = null) =>
            Task.FromResult(ReviewSharedAnalysisSettingsTests.Recording());
        public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ReviewSharedAnalysisSettingsTests.Algorithm());
        public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
            Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken)
        {
            Request = request;
            Started.SetResult();
            return Completed.Task;
        }
        public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId, long cursorSample,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Stale Run must not be read.");
    }
}

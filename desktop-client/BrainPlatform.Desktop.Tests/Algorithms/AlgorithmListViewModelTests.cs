using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmListViewModelTests
{
    [Fact]
    public void StaticRange_RejectsReversedRange()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseStaticRangeOrThrow("10", "2", 20));
    }

    [Fact]
    public void StaticRange_RejectsRangeOutsideRegisteredRecording()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseStaticRangeOrThrow("0", "20.001", 20));
    }

    [Fact]
    public void StaticRange_RejectsNonFiniteInput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseStaticRangeOrThrow("NaN", "10", 20));
    }

    [Fact]
    public void StaticRange_PreservesExplicitSeconds()
    {
        var range = AlgorithmRunConfiguration.ParseStaticRangeOrThrow("1.250", "9.875", 20);

        Assert.Equal(new TimeRange(1.25, 9.875), range);
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesRbpButNotDynamicOnlyOrUnavailableItems()
    {
        var rbp = new AlgorithmCatalogItem("official", "rbp", "offline-spectral-v3", "相对频段功率", "RBP", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var unavailable = rbp with { Id = "brainbeat", IsRunnable = false };
        var dynamicOnly = rbp with { Id = "dynamic", Modes = ["dynamic"] };

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(rbp));
        Assert.False(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(unavailable));
        Assert.False(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesPeakFrequency()
    {
        var peak = new AlgorithmCatalogItem("official", "peak_frequency", "official-peak-frequency-v1", "峰频率", "Peak Frequency", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(peak));
    }

    [Theory]
    [InlineData("10", "2")]
    [InlineData("5", "5")]
    [InlineData("-1", "10")]
    [InlineData("NaN", "10")]
    public void FrequencyBand_RejectsInvalidRange(string low, string high)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(low, high, 200));
    }

    [Fact]
    public void FrequencyBand_RejectsNyquistBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseFrequencyBandOrThrow("1", "100", 200));
    }

    [Fact]
    public void FrequencyBand_PreservesExplicitValues()
    {
        var band = AlgorithmRunConfiguration.ParseFrequencyBandOrThrow("1.25", "30.5", 256);

        Assert.Equal(1.25, band.LowHz);
        Assert.Equal(30.5, band.HighHz);
    }

    [Fact]
    public void PeakFrequencyConfig_ContainsFrequencyBandAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "peak_frequency", "official-peak-frequency-v1", "峰频率", "Peak Frequency", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(
            algorithm, "O1", new TimeRange(0, 20), new AlgorithmRunConfiguration.FrequencyBand(1, 30));

        Assert.Equal("peak_frequency", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("official-peak-frequency-v1", config.GetProperty("scientific_version").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal(1, config.GetProperty("low_hz").GetDouble());
        Assert.Equal(30, config.GetProperty("high_hz").GetDouble());
        Assert.False(config.TryGetProperty("dynamic_window_s", out _));
    }

    [Fact]
    public void DynamicConfig_ContainsWindowAndStepWithoutChangingRange()
    {
        var algorithm = new AlgorithmCatalogItem("official", "psd", "offline-spectral-v3", "功率谱密度", "PSD", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [5, 10, 20], 10, 1, true), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, "O1", new TimeRange(2, 18), "dynamic", windowSeconds: 10, stepSeconds: 1);

        Assert.Equal("dynamic", config.GetProperty("mode").GetString());
        Assert.Equal(2, config.GetProperty("time").GetProperty("start_s").GetDouble());
        Assert.Equal(18, config.GetProperty("time").GetProperty("end_s").GetDouble());
        Assert.Equal(10, config.GetProperty("dynamic_window_s").GetDouble());
        Assert.Equal(1, config.GetProperty("refresh_step_s").GetDouble());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("NaN")]
    [InlineData("-1")]
    public void DynamicStep_RejectsNonPositiveValues(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParsePositiveSecondsOrThrow(value, "刷新步长"));
    }

    [Fact]
    public void DynamicScalarPreview_PreservesNullAndWindowState()
    {
        var run = new AnalysisRunResponse("run", "recording", "official_algorithm", "completed", "v1", null, null,
            JsonDocument.Parse("{\"metric\":{\"output\":{\"unit\":\"Hz\"},\"series\":[{\"time_s\":5,\"value\":10.25,\"analysis_state\":\"Complete\",\"quality\":{\"status\":\"clean\"}},{\"time_s\":6,\"value\":null,\"analysis_state\":\"Unavailable\",\"quality\":{\"status\":\"unavailable\"}}]}}").RootElement,
            null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var points = DynamicResultPreview.ParseScalar(run);

        Assert.Equal(2, points.Count);
        Assert.Equal("10.25", points[0].ValueText);
        Assert.Equal("不可用", points[1].ValueText);
        Assert.Equal("Unavailable", points[1].State);
    }

    [Fact]
    public void DynamicStructuredPreview_ParsesWindowFailureCode()
    {
        var preview = new StructuredPreviewResponse("run", new RunArtifact("a", "run", "dynamic", "x", "application/octet-stream", 1, "hash", null, new Dictionary<string, int[]>() { ["psd"] = [2, 3] }, DateTimeOffset.UtcNow),
            null, ["O1"], null, null, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
            [JsonDocument.Parse("{\"start_s\":0,\"end_s\":5,\"state\":\"Rejected\",\"quality\":\"gate_failed\",\"failure\":{\"code\":\"GAP\"}}").RootElement],
            new Dictionary<string, int> { ["Rejected"] = 1 }, null, "v1", "impl");

        var rows = DynamicResultPreview.ParseStructured(preview);

        Assert.Single(rows);
        Assert.Equal("已拒绝", rows[0].State);
        Assert.Equal("GAP", rows[0].Failure);
    }

    [Fact]
    public void DynamicPreview_ReleasesWindowsByCursorAndCanReset()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        catalog.SelectedAlgorithm = Algorithm("psd") with { Modes = ["static", "dynamic"] };
        catalog.SelectedAnalysisMode = "动态";
        catalog.DynamicWindowRows.Add(new DynamicWindowRow(0, 4, "Partial", "warmup", ""));
        catalog.DynamicWindowRows.Add(new DynamicWindowRow(0, 10, "Complete", "clean", ""));
        catalog.DynamicWindowRows.Add(new DynamicWindowRow(1, 11, "Complete", "clean", ""));

        catalog.StepDynamicPreviewCommand.Execute(null);

        Assert.Equal(1, catalog.DynamicPreviewCursorSeconds);
        Assert.Empty(catalog.DynamicPreviewRows);

        catalog.DynamicPreviewCursorSecondsForTest(10);
        catalog.StepDynamicPreviewCommand.Execute(null);
        Assert.Equal(3, catalog.DynamicPreviewRows.Count);

        catalog.ResetDynamicPreviewCommand.Execute(null);
        Assert.Equal(0, catalog.DynamicPreviewCursorSeconds);
        Assert.Empty(catalog.DynamicPreviewRows);
    }

    [Fact]
    public void DynamicPreviewContext_ForwardsCollectionStateChanges()
    {
        var catalog = new AlgorithmListViewModel(new UnsupportedAlgorithmClient(), new OperationNotificationCenter());
        var context = new AnalysisContextViewModel(catalog);
        catalog.SelectedAlgorithm = Algorithm("psd") with { Modes = ["static", "dynamic"] };
        catalog.SelectedAnalysisMode = "动态";
        catalog.DynamicWindowRows.Add(new DynamicWindowRow(0, 10, "Complete", "clean", ""));
        catalog.DynamicPreviewCursorSecondsForTest(10);

        var changed = new List<string>();
        context.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null) changed.Add(args.PropertyName);
        };

        catalog.ResetDynamicPreviewCommand.Execute(null);

        Assert.Empty(context.DynamicPreviewRows);
        Assert.Contains(nameof(AnalysisContextViewModel.DynamicPreviewRows), changed);
        Assert.Contains(nameof(AnalysisContextViewModel.DynamicPreviewCursorText), changed);
    }

    private static AlgorithmCatalogItem Algorithm(string id) => new(
        "official", id, "v1", id, id.ToUpperInvariant(), "", [], ["static"], default,
        new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

    private sealed class UnsupportedAlgorithmClient : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesBandRatio()
    {
        var ratio = new AlgorithmCatalogItem("official", "band_ratio", "official-band-ratio-v1", "频段功率比", "Band Ratio", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(ratio));
    }

    [Fact]
    public void BandRatioBands_ValidateBothBandsAgainstNyquist()
    {
        var bands = AlgorithmRunConfiguration.ParseBandRatioBandsOrThrow("4", "8", "8", "13", 256);

        Assert.Equal(new AlgorithmRunConfiguration.FrequencyBand(4, 8), bands.Numerator);
        Assert.Equal(new AlgorithmRunConfiguration.FrequencyBand(8, 13), bands.Denominator);
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunConfiguration.ParseBandRatioBandsOrThrow("4", "8", "8", "128", 256));
    }

    [Fact]
    public void BandRatioConfig_ContainsBothBandsAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "band_ratio", "official-band-ratio-v1", "频段功率比", "Band Ratio", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(
            algorithm, "O1", new TimeRange(0, 20), ratioBands: new AlgorithmRunConfiguration.BandRatioBands(
                new AlgorithmRunConfiguration.FrequencyBand(4, 8), new AlgorithmRunConfiguration.FrequencyBand(8, 13)));

        Assert.Equal("band_ratio", config.GetProperty("algorithm_id").GetString());
        Assert.Equal(4, config.GetProperty("numerator_low_hz").GetDouble());
        Assert.Equal(8, config.GetProperty("numerator_high_hz").GetDouble());
        Assert.Equal(8, config.GetProperty("denominator_low_hz").GetDouble());
        Assert.Equal(13, config.GetProperty("denominator_high_hz").GetDouble());
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesFaaButNotDynamicOnly()
    {
        var faa = new AlgorithmCatalogItem("official", "faa", "official-faa-v1", "额叶 Alpha 不对称性", "FAA", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var dynamicOnly = faa with { Modes = ["dynamic"] };

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(faa));
        Assert.False(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void FaaConfig_ContainsDistinctSourceChannelsAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "faa", "official-faa-v1", "额叶 Alpha 不对称性", "FAA", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(algorithm, "F3", new TimeRange(0, 20), f4Channel: "F4");

        Assert.Equal("faa", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("F3", config.GetProperty("channel").GetString());
        Assert.Equal("F4", config.GetProperty("f4_channel").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesIapfButNotDynamicOnly()
    {
        var iapf = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "个体 Alpha 峰频率", "IAPF", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var dynamicOnly = iapf with { Modes = ["dynamic"] };

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(iapf));
        Assert.False(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void IapfConfig_ContainsSingleChannelAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "个体 Alpha 峰频率", "IAPF", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(algorithm, "O1", new TimeRange(0, 20));

        Assert.Equal("iapf", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesThetaBeta()
    {
        var thetaBeta = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta 比值", "Theta/Beta", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmRunConfiguration.IsSupportedStaticAlgorithm(thetaBeta));
    }

    [Fact]
    public void ThetaBetaConfig_ContainsSingleChannelAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta 比值", "Theta/Beta", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmRunConfiguration.BuildStaticRunConfig(algorithm, "O1", new TimeRange(0, 20));

        Assert.Equal("theta_beta", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
    }

    [Fact]
    public void RbpSummary_FormatsBackendBandValuesWithoutRecomputing()
    {
        var run = new AnalysisRunResponse("run-rbp", "recording", "official_algorithm", "completed", "offline-spectral-v3", null, null,
            JsonDocument.Parse("{\"metric\":{\"output\":{\"value\":null,\"unit\":\"ratio\"},\"band_values\":{\"delta\":0.4,\"theta\":0.1,\"alpha\":0.3,\"beta\":0.2},\"channel\":\"O1\",\"quality\":\"clean\"}}").RootElement,
            null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var summary = AlgorithmResultFormatter.BuildResultSummary(run);

        Assert.Contains("Delta：0.4", summary);
        Assert.Contains("Alpha：0.3", summary);
        Assert.Contains("单位：ratio", summary);
    }

    [Fact]
    public void DynamicSummary_UsesLatestUsableWindowInsteadOfRejectedFirstWindow()
    {
        var run = new AnalysisRunResponse("run-rbp-dynamic", "recording", "official_algorithm", "completed", "offline-spectral-v3", null, null,
            JsonDocument.Parse("{\"metric\":{\"output\":{\"value\":null,\"unit\":\"ratio\"},\"series\":[{\"time_s\":4,\"value\":null,\"analysis_state\":\"Rejected\",\"quality\":{\"status\":\"gate_failed\"}},{\"time_s\":10,\"band_values\":{\"delta\":0.4,\"theta\":0.1,\"alpha\":0.3,\"beta\":0.2},\"analysis_state\":\"Complete\",\"quality\":{\"status\":\"clean\"}}]}}").RootElement,
            null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var summary = AlgorithmResultFormatter.BuildResultSummary(run);

        Assert.Contains("最近有效窗口 10 秒", summary);
        Assert.Contains("Delta：0.4", summary);
        Assert.DoesNotContain("指标：不可用", summary);
    }

    [Fact]
    public void Provenance_UsesOfficialRunVersionBeforeSpectralImplementationVersion()
    {
        var run = new AnalysisRunResponse("run-peak", "recording", "official_algorithm", "completed", "official-peak-frequency-v1", null, null,
            null, JsonDocument.Parse("{\"scientific_algorithm_version\":\"offline-spectral-v3\",\"mode\":\"static\",\"channel\":\"Fp1\"}").RootElement,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var provenance = AlgorithmResultFormatter.BuildProvenance(run);

        Assert.Contains("科学版本：official-peak-frequency-v1", provenance);
    }
}

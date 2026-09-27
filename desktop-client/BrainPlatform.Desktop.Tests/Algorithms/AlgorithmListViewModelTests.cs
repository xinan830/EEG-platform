using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmListViewModelTests
{
    [Fact]
    public void StaticRange_RejectsReversedRange()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("10", "2", 20));
    }

    [Fact]
    public void StaticRange_RejectsRangeOutsideRegisteredRecording()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("0", "20.001", 20));
    }

    [Fact]
    public void StaticRange_RejectsNonFiniteInput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("NaN", "10", 20));
    }

    [Fact]
    public void StaticRange_PreservesExplicitSeconds()
    {
        var range = AlgorithmListViewModel.ParseStaticRangeOrThrow("1.250", "9.875", 20);

        Assert.Equal(new TimeRange(1.25, 9.875), range);
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesRbpButNotDynamicOnlyOrUnavailableItems()
    {
        var rbp = new AlgorithmCatalogItem("official", "rbp", "offline-spectral-v3", "相对频段功率", "RBP", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var unavailable = rbp with { Id = "brainbeat", IsRunnable = false };
        var dynamicOnly = rbp with { Id = "dynamic", Modes = ["dynamic"] };

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(rbp));
        Assert.False(AlgorithmListViewModel.IsSupportedStaticAlgorithm(unavailable));
        Assert.False(AlgorithmListViewModel.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesPeakFrequency()
    {
        var peak = new AlgorithmCatalogItem("official", "peak_frequency", "official-peak-frequency-v1", "峰频率", "Peak Frequency", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(peak));
    }

    [Theory]
    [InlineData("10", "2")]
    [InlineData("5", "5")]
    [InlineData("-1", "10")]
    [InlineData("NaN", "10")]
    public void FrequencyBand_RejectsInvalidRange(string low, string high)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseFrequencyBandOrThrow(low, high, 200));
    }

    [Fact]
    public void FrequencyBand_RejectsNyquistBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseFrequencyBandOrThrow("1", "100", 200));
    }

    [Fact]
    public void FrequencyBand_PreservesExplicitValues()
    {
        var band = AlgorithmListViewModel.ParseFrequencyBandOrThrow("1.25", "30.5", 256);

        Assert.Equal(1.25, band.LowHz);
        Assert.Equal(30.5, band.HighHz);
    }

    [Fact]
    public void PeakFrequencyConfig_ContainsFrequencyBandAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "peak_frequency", "official-peak-frequency-v1", "峰频率", "Peak Frequency", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmListViewModel.BuildStaticRunConfig(
            algorithm, "O1", new TimeRange(0, 20), new AlgorithmListViewModel.FrequencyBand(1, 30));

        Assert.Equal("peak_frequency", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("official-peak-frequency-v1", config.GetProperty("scientific_version").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal(1, config.GetProperty("low_hz").GetDouble());
        Assert.Equal(30, config.GetProperty("high_hz").GetDouble());
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesBandRatio()
    {
        var ratio = new AlgorithmCatalogItem("official", "band_ratio", "official-band-ratio-v1", "频段功率比", "Band Ratio", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(ratio));
    }

    [Fact]
    public void BandRatioBands_ValidateBothBandsAgainstNyquist()
    {
        var bands = AlgorithmListViewModel.ParseBandRatioBandsOrThrow("4", "8", "8", "13", 256);

        Assert.Equal(new AlgorithmListViewModel.FrequencyBand(4, 8), bands.Numerator);
        Assert.Equal(new AlgorithmListViewModel.FrequencyBand(8, 13), bands.Denominator);
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseBandRatioBandsOrThrow("4", "8", "8", "128", 256));
    }

    [Fact]
    public void BandRatioConfig_ContainsBothBandsAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "band_ratio", "official-band-ratio-v1", "频段功率比", "Band Ratio", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmListViewModel.BuildStaticRunConfig(
            algorithm, "O1", new TimeRange(0, 20), ratioBands: new AlgorithmListViewModel.BandRatioBands(
                new AlgorithmListViewModel.FrequencyBand(4, 8), new AlgorithmListViewModel.FrequencyBand(8, 13)));

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

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(faa));
        Assert.False(AlgorithmListViewModel.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void FaaConfig_ContainsDistinctSourceChannelsAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "faa", "official-faa-v1", "额叶 Alpha 不对称性", "FAA", "", [], ["static"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmListViewModel.BuildStaticRunConfig(algorithm, "F3", new TimeRange(0, 20), f4Channel: "F4");

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

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(iapf));
        Assert.False(AlgorithmListViewModel.IsSupportedStaticAlgorithm(dynamicOnly));
    }

    [Fact]
    public void IapfConfig_ContainsSingleChannelAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "iapf", "official-iapf-v2", "个体 Alpha 峰频率", "IAPF", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmListViewModel.BuildStaticRunConfig(algorithm, "O1", new TimeRange(0, 20));

        Assert.Equal("iapf", config.GetProperty("algorithm_id").GetString());
        Assert.Equal("O1", config.GetProperty("channel").GetString());
        Assert.Equal("static", config.GetProperty("mode").GetString());
    }

    [Fact]
    public void StaticAlgorithmAllowList_IncludesThetaBeta()
    {
        var thetaBeta = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta 比值", "Theta/Beta", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);

        Assert.True(AlgorithmListViewModel.IsSupportedStaticAlgorithm(thetaBeta));
    }

    [Fact]
    public void ThetaBetaConfig_ContainsSingleChannelAndStaticContract()
    {
        var algorithm = new AlgorithmCatalogItem("official", "theta_beta", "official-theta-beta-v2", "Theta/Beta 比值", "Theta/Beta", "", [], ["static", "dynamic"],
            default, new DynamicAnalysisPolicy(4, [], 10, 1, false), "available", true, null, null, null);
        var config = AlgorithmListViewModel.BuildStaticRunConfig(algorithm, "O1", new TimeRange(0, 20));

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

        var summary = AlgorithmListViewModel.BuildResultSummary(run);

        Assert.Contains("Delta：0.4", summary);
        Assert.Contains("Alpha：0.3", summary);
        Assert.Contains("单位：ratio", summary);
    }

    [Fact]
    public void Provenance_UsesOfficialRunVersionBeforeSpectralImplementationVersion()
    {
        var run = new AnalysisRunResponse("run-peak", "recording", "official_algorithm", "completed", "official-peak-frequency-v1", null, null,
            null, JsonDocument.Parse("{\"scientific_algorithm_version\":\"offline-spectral-v3\",\"mode\":\"static\",\"channel\":\"Fp1\"}").RootElement,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var provenance = AlgorithmListViewModel.BuildProvenance(run);

        Assert.Contains("科学版本：official-peak-frequency-v1", provenance);
    }
}

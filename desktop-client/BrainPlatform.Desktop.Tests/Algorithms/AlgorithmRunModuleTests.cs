namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmRunModuleTests
{
    [Theory]
    [InlineData("psd", true, false)]
    [InlineData("stft", true, false)]
    [InlineData("peak_frequency", false, true)]
    [InlineData("rbp", false, true)]
    [InlineData("iapf", false, true)]
    [InlineData("faa", true, true)]
    [InlineData("band_ratio", false, true)]
    [InlineData("theta_beta", false, true)]
    [InlineData("brainbeat", false, true)]
    public void RegisteredModules_KeepExistingResultAndNotchCapabilities(string id, bool notch, bool scalar)
    {
        var module = AlgorithmRunResultHandlerRegistry.For(id);
        Assert.Equal(id, module.AlgorithmId);
        Assert.Equal(notch, module.SupportsNotch);
        Assert.Equal(scalar, module.UsesDynamicScalarSeries);
    }

    [Theory]
    [InlineData("peak_frequency", "peak_frequency_hz")]
    [InlineData("iapf", "iapf_hz")]
    [InlineData("band_ratio", "ratio")]
    [InlineData("theta_beta", "theta_beta")]
    public void ScalarFullPreview_UsesSharedDeclarativeMetricSpec(string id, string valueField)
    {
        var module = AlgorithmRunResultHandlerRegistry.For(id);

        Assert.NotNull(module.FullRunMetricWindowSpec);
        Assert.Equal(valueField, module.FullRunMetricWindowSpec!.ValueField);
        Assert.True(module.SupportsFullRunMetricWindowPreview);
    }

    [Fact]
    public void RbpV3FullPreview_UsesStructuredWindowProjection()
    {
        var spec = AlgorithmRunResultHandlerRegistry.For("rbp").FullRunMetricWindowSpec;

        Assert.Null(spec);
        Assert.True(AlgorithmRunResultHandlerRegistry.For("rbp").SupportsFullRunWindowPreview);
        Assert.False(AlgorithmRunResultHandlerRegistry.For("rbp").SupportsFullRunMetricWindowPreview);
    }

    [Theory]
    [InlineData("faa")]
    [InlineData("brainbeat")]
    public void PairedAlgorithms_DoNotClaimFullPreviewWithoutPairProvenance(string id)
    {
        var module = AlgorithmRunResultHandlerRegistry.For(id);

        Assert.Null(module.FullRunMetricWindowSpec);
        Assert.False(module.SupportsFullRunMetricWindowPreview);
    }

    [Fact]
    public void UnregisteredAlgorithm_CannotFallBackToAnotherResultType()
    {
        Assert.False(AlgorithmRunResultHandlerRegistry.Contains("unknown_algorithm"));
        Assert.Throws<InvalidOperationException>(() => AlgorithmRunResultHandlerRegistry.For("unknown_algorithm"));
    }

    [Theory]
    [InlineData("psd")]
    [InlineData("stft")]
    [InlineData("peak_frequency")]
    public void FrequencyModules_ValidateNyquistAndPreserveRequestedBand(string id)
    {
        var module = AlgorithmRunResultHandlerRegistry.For(id);
        var parameters = module.ParseParameters(Inputs());
        Assert.Equal(new AlgorithmRunConfiguration.FrequencyBand(1, 50), parameters.FrequencyBand);
        Assert.Throws<InvalidOperationException>(() => module.ParseParameters(Inputs() with { SamplingRateHz = 100 }));
    }

    [Theory]
    [InlineData("关闭", 0)]
    [InlineData("50 Hz", 50)]
    [InlineData("60 Hz", 60)]
    public void SpectralModules_UseSelectedNotchInBothModes(string text, double expected)
    {
        foreach (var id in new[] { "psd", "stft" })
            Assert.Equal(expected, AlgorithmRunResultHandlerRegistry.For(id)
                .ParseParameters(Inputs() with { NotchFrequency = text }).NotchHz);
    }

    [Fact]
    public void Faa_RequiresTwoDistinctRegisteredChannels()
    {
        var module = AlgorithmRunResultHandlerRegistry.For("faa");
        Assert.Equal("F4", module.ParseParameters(Inputs()).F4Channel);
        Assert.Throws<InvalidOperationException>(() => module.ParseParameters(Inputs() with { F4Channel = "missing" }));
        Assert.Throws<InvalidOperationException>(() => module.ParseParameters(Inputs() with { F4Channel = "F3" }));
    }

    [Fact]
    public void TnkGroup_SerializesOneCanonicalRoleMapping()
    {
        var algorithm = AlgorithmParameterValidationTests.Catalog() with
        {
            Id = "tnk_biofeedback",
        };
        var roleChannels = new Dictionary<string, string>
        {
            ["Fz"] = "Fz-AVG", ["Pz"] = "Pz-AVG", ["Oz"] = "Oz-AVG", ["F3"] = "F3-AVG", ["F4"] = "F4-AVG",
        };

        var config = AlgorithmRunConfiguration.BuildRunConfig(
            algorithm, "Fp1-REF", new TimeRange(0, 10), "static", roleChannels: roleChannels);

        Assert.Equal("Fz-AVG", config.GetProperty("channel").GetString());
        Assert.Equal("Pz-AVG", config.GetProperty("tnk_role_mapping").GetProperty("Pz").GetString());
        Assert.Equal(roleChannels, AlgorithmRunResultHandlerRegistry.For("tnk_biofeedback")
            .ParseParameters(Inputs() with
            {
                Channel = "Fp1-REF", Channels = roleChannels.Values.ToArray(), RoleChannels = roleChannels,
            }).RoleChannels);
        Assert.Equal("Fz-AVG", AlgorithmRunResultHandlerRegistry.For("tnk_biofeedback")
            .ParseParameters(Inputs() with
            {
                Channel = string.Empty, Channels = roleChannels.Values.ToArray(), RoleChannels = roleChannels,
            }).PrimaryChannel);
    }

    [Fact]
    public void TnkRoleChannelSelection_AppliesOnlyTheExplicitlyResolvedDefault()
    {
        var resolved = new TnkRoleChannelSelection("secondary_channel", "Oz 分析通道", "Oz-REF");
        var unresolved = new TnkRoleChannelSelection("secondary_channel", "Oz 分析通道", string.Empty);

        Assert.Equal("Oz-REF", resolved.SelectedChannel);
        Assert.Equal(string.Empty, unresolved.SelectedChannel);
    }

    [Fact]
    public void TnkGroup_RejectsRepeatedOrUnavailableRoleMappingsBeforeSubmission()
    {
        var inputs = Inputs() with
        {
            Channel = "Fz-REF",
            Channels = ["Fz-REF", "Pz-REF", "Oz-REF", "F3-REF", "F4-REF"],
            RoleChannels = new Dictionary<string, string>
            {
                ["Fz"] = "Fz-REF", ["Pz"] = "Fz-REF", ["Oz"] = "Oz-REF", ["F3"] = "F3-REF", ["F4"] = "F4-REF",
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            AlgorithmRunResultHandlerRegistry.For("tnk_biofeedback").ParseParameters(inputs));
        Assert.Contains("不同于其他角色", error.Message, StringComparison.Ordinal);
        Assert.Contains("分析参数", error.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => AlgorithmRunResultHandlerRegistry.For("tnk_biofeedback")
            .ParseParameters(inputs with { RoleChannels = new Dictionary<string, string>() }));
    }

    [Fact]
    public void Stft_RejectsShortRangeWithoutChangingOtherAlgorithms()
    {
        var inputs = Inputs() with { Range = new TimeRange(0, 3) };
        Assert.Throws<InvalidOperationException>(() => AlgorithmRunResultHandlerRegistry.For("stft").ParseParameters(inputs));
        Assert.NotNull(AlgorithmRunResultHandlerRegistry.For("psd").ParseParameters(inputs));
    }

    [Fact]
    public void BandRatio_ValidatesBothBandsAndKeepsTheirRoles()
    {
        var module = AlgorithmRunResultHandlerRegistry.For("band_ratio");
        var bands = module.ParseParameters(Inputs()).RatioBands;
        Assert.Equal(new AlgorithmRunConfiguration.FrequencyBand(4, 8), bands?.Numerator);
        Assert.Equal(new AlgorithmRunConfiguration.FrequencyBand(8, 13), bands?.Denominator);
        Assert.Throws<InvalidOperationException>(() => module.ParseParameters(Inputs() with { NumeratorHighText = "2" }));
        Assert.Throws<InvalidOperationException>(() => module.ParseParameters(Inputs() with { DenominatorHighText = "8" }));
    }

    private static AlgorithmRunInputs Inputs() => new(new TimeRange(0, 20), "F3", "F4", ["F3", "F4"],
        2000, "1", "50", "4", "8", "8", "13", "50 Hz");
}

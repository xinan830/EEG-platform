namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmRunModuleTests
{
    [Theory]
    [InlineData("psd", true, false)]
    [InlineData("stft", true, false)]
    [InlineData("peak_frequency", false, true)]
    [InlineData("rbp", false, true)]
    [InlineData("iapf", false, true)]
    [InlineData("faa", false, true)]
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

using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmParameterValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void BoundsAreInclusive(double value) =>
        AlgorithmParameterValidation.ValidateOrThrow(Catalog(), JsonSerializer.SerializeToElement(new { high_hz = value }));

    [Theory]
    [InlineData(0.5, "不能小于 1 Hz")]
    [InlineData(40, "不能大于 30 Hz")]
    public void OutOfBoundsUsesChineseLabelAndObservedValue(double value, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AlgorithmParameterValidation.ValidateOrThrow(Catalog(), JsonSerializer.SerializeToElement(new { high_hz = value })));
        Assert.Contains("频段上限", error.Message);
        Assert.Contains(expected, error.Message);
        Assert.Contains("当前为", error.Message);
    }

    [Fact]
    public void BoundsComeFromCatalogRatherThanAlgorithmId()
    {
        var catalog = Catalog();
        catalog = catalog with { Parameters = [catalog.Parameters[0] with { Maximum = 50 }] };
        AlgorithmParameterValidation.ValidateOrThrow(catalog, JsonSerializer.SerializeToElement(new { high_hz = 40 }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"high_hz\":null}")]
    public void BackendStillOwnsOmittedDefaults(string json) =>
        AlgorithmParameterValidation.ValidateOrThrow(Catalog(), JsonDocument.Parse(json).RootElement);

    [Theory]
    [InlineData("psd", "static", 0)]
    [InlineData("psd", "dynamic", 50)]
    [InlineData("psd", "dynamic", 60)]
    [InlineData("stft", "static", 0)]
    [InlineData("stft", "static", 60)]
    [InlineData("stft", "dynamic", 50)]
    public void SerializedNotchMatchesSelection(string id, string mode, double notch)
    {
        var algorithm = Catalog() with { Id = id };
        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, "F3", new TimeRange(0, 30), mode,
            new AlgorithmRunConfiguration.FrequencyBand(1, 50), windowSeconds: mode == "dynamic" ? 5 : null,
            stepSeconds: mode == "dynamic" ? 1 : null, notchHz: notch);
        Assert.Equal(notch, config.GetProperty("notch_hz").GetDouble());
        Assert.Equal(mode, config.GetProperty("mode").GetString());
    }

    internal static AlgorithmCatalogItem Catalog() => new("official", "peak_frequency", "official-peak-frequency-v1",
        "峰频率", "Peak", "", [new AlgorithmParameter("high_hz", "频段上限", "number", true,
            null, "Hz", 1, 30, null, true, "both", [], "")], ["static", "dynamic"], default,
        new DynamicAnalysisPolicy(4, [5, 10], 5, 1, true), "available", true, null, null, null);
}

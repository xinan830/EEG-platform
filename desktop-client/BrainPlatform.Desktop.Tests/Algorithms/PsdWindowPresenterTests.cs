using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class PsdWindowPresenterTests
{
    [Theory]
    [InlineData("V^2/Hz", 1e12)]
    [InlineData("uV^2/Hz", 1)]
    public void Explicit_units_are_converted_once_and_labelled_correctly(string unit, double factor)
    {
        var state = new PsdPreviewState();
        PsdWindowPresenter.Apply(state, Response(unit: unit));
        Assert.Equal(factor, state.Points[0]!.Value);
        Assert.Equal("uV^2/Hz", state.ValueUnit);
        Assert.Equal("Hz", state.FrequencyUnit);
        Assert.Equal(0.2, state.BandShares[0].Share);
        Assert.Null(state.StructuredPreview);
    }

    [Theory]
    [InlineData("axis")]
    [InlineData("psd")]
    [InlineData("ratio")]
    [InlineData("shape")]
    public void Invalid_unit_or_shape_clears_stale_chart_without_partial_new_projection(string invalid)
    {
        var state = new PsdPreviewState();
        PsdWindowPresenter.Apply(state, Response());
        var response = Response(axis: invalid == "axis" ? "s" : "Hz", unit: invalid == "psd" ? "V" : "V^2/Hz",
            ratio: invalid == "ratio" ? "percent" : "ratio", shares: invalid == "shape" ? [0.2] : null);
        Assert.Throws<InvalidOperationException>(() => PsdWindowPresenter.Apply(state, response));
        Assert.False(state.HasPreview);
        Assert.Empty(state.Points);
        Assert.Empty(state.BandShares);
        Assert.Null(state.WindowPreview);
    }

    private static DynamicWindowResponse Response(string axis = "Hz", string unit = "V^2/Hz",
        string ratio = "ratio", double[]? shares = null) => new("session", "window", "request", "ready",
        null, null, new DynamicWindowRange(0, 1000, 0, 5), "clean", null,
        new DynamicWindowResultResponse(null, "ready", "frequency_series",
            new Dictionary<string, JsonElement> { ["frequency_hz"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d }) },
            new Dictionary<string, string> { ["frequency_hz"] = axis },
            new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(new[] { 1d, 2d }),
                ["band_share"] = JsonSerializer.SerializeToElement(shares ?? [0.2, 0.2, 0.2, 0.2, 0.2]) },
            new Dictionary<string, string> { ["psd"] = unit, ["band_share"] = ratio }, "clean", null, null, null));
}

using System.Text.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Psd;
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
        Assert.Equal("µV²/Hz", state.ValueUnit);
        Assert.Equal("Hz", state.FrequencyUnit);
        Assert.Equal(0.2, state.BandShares[0].Share);
        Assert.Null(state.StructuredPreview);
    }

    [Fact]
    public void Tooltip_formats_decimal_values_without_format_specifier_suffix_and_uses_display_unit()
    {
        Assert.Equal("10.1", PsdSpectrumChart.FormatTooltipNumber(10.1));
        Assert.Equal("33.1", PsdSpectrumChart.FormatTooltipNumber(33.1));
        Assert.Equal("µV²/Hz", PsdSpectrumChart.FormatUnit("uV^2/Hz"));
    }

    [Fact]
    public void Hover_snaps_to_nearest_sample_only_when_pointer_is_near_the_spectrum()
    {
        OnSta(() =>
        {
            var chart = new PsdSpectrumChart
            {
                Points = Enumerable.Range(0, 51).Select(value => new PsdPreviewPoint(value, value)).ToArray(),
            };
            chart.Measure(new Size(400, 200));
            chart.Arrange(new Rect(0, 0, 400, 200));
            chart.UpdateLayout();
            var mousePosition = new Point(200, 108);

            UpdateHover(chart, mousePosition);

            var marker = (Point?)typeof(PsdSpectrumChart).GetField("hoverScreenPoint",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart);
            Assert.NotNull(marker);
            Assert.NotEqual(mousePosition, marker);
            Assert.Equal(200, marker.Value.X);
            Assert.InRange(marker.Value.Y, 109, 110);
            Assert.Equal(25d, typeof(PsdSpectrumChart).GetField("hoverFreq",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart));
            Assert.Equal(25d, typeof(PsdSpectrumChart).GetField("hoverVal",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart));

            UpdateHover(chart, new Point(200, 10));

            Assert.False((bool)typeof(PsdSpectrumChart).GetField("isHovered",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart)!);
            Assert.Null(typeof(PsdSpectrumChart).GetField("hoverScreenPoint",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chart));
        });
    }

    private static void UpdateHover(PsdSpectrumChart chart, Point position) =>
        typeof(PsdSpectrumChart).GetMethod("UpdateHover", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(chart, new object?[] { position });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
            new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(new[] { new[] { 1d, 2d } }),
                ["band_share"] = JsonSerializer.SerializeToElement(new[] { shares ?? [0.2, 0.2, 0.2, 0.2, 0.2] }) },
            new Dictionary<string, string> { ["psd"] = unit, ["band_share"] = ratio }, "clean", null, null, null));
}

using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class PsdTopomapDensityTests
{
    [Fact]
    public void Presenter_keeps_backend_density_without_recomputing_or_converting_it()
    {
        var model = new PsdReviewAnalysisViewModel { SelectedTopomapBandId = "alpha", SelectedView = "脑电地形图" };
        var field = Field("psd.band-mean-density.alpha", "V^2/Hz");

        model.ApplyTopomap(Response(field));

        Assert.Same(field, model.TopomapField);
        Assert.Equal(2e-12, model.TopomapField!.Points[0].Value);
        Assert.Equal(new double[] { 8, 13 }, model.TopomapField.FrequencyRangeHz);
        Assert.Equal("10–20 s", model.CurrentRangeText);
        Assert.Equal("µV²/Hz", TopomapCanvas.DisplayUnit(field.Unit));
        Assert.Equal("2", TopomapCanvas.FormatScaleValue(field.Points[0].Value!.Value, field.Unit));
    }

    [Fact]
    public void Presenter_keeps_five_band_fields_stable_and_clears_them_as_a_group()
    {
        var bands = new[] { (Id: "delta", Low: 1d, High: 4d), (Id: "theta", Low: 4d, High: 8d),
            (Id: "alpha", Low: 8d, High: 13d), (Id: "beta", Low: 13d, High: 30d),
            (Id: "gamma", Low: 30d, High: 50d) };
        var model = new PsdReviewAnalysisViewModel { SelectedTopomapBandId = "" };
        var fields = bands.Select(item => Field($"psd.band-mean-density.{item.Id}", "V^2/Hz") with
        {
            FrequencyRangeHz = [item.Low, item.High],
        }).ToArray();

        model.ApplyTopomap(Response(fields));
        var items = model.TopomapBands.ToArray();
        model.ApplyTopomap(Response(fields));

        Assert.True(model.IsAllTopomapBandsSelected);
        Assert.Null(model.SelectedTopomapFieldId);
        Assert.Null(model.TopomapField);
        Assert.Equal(5, model.TopomapBands.Count);
        Assert.Equal(bands.Select(item => item.Low),
            model.TopomapBands.Select(item => item.Field.FrequencyRangeHz![0]));
        Assert.Equal(bands.Select(item => item.High),
            model.TopomapBands.Select(item => item.Field.FrequencyRangeHz![1]));
        Assert.All(items.Zip(model.TopomapBands), pair => Assert.Same(pair.First, pair.Second));

        model.ClearTopomap();

        Assert.Empty(model.TopomapBands);
    }

    [Theory]
    [InlineData("psd.band-power.alpha", "uV^2")]
    [InlineData("psd.band-mean-density.alpha", "uV^2")]
    [InlineData("psd.band-mean-density.theta", "V^2/Hz")]
    public void Presenter_rejects_power_units_and_another_band(string fieldId, string unit)
    {
        var model = new PsdReviewAnalysisViewModel { SelectedTopomapBandId = "alpha" };

        Assert.Throws<InvalidOperationException>(() => model.ApplyTopomap(Response(Field(fieldId, unit))));

        Assert.Null(model.TopomapField);
    }

    private static TopomapField Field(string fieldId, string unit) => new(
        fieldId, "Alpha 平均 PSD (8–13 Hz)", unit, null,
        [new TopomapPoint("Cz", "Cz", 0, 0, 2e-12, "included", null)], null,
        "unavailable", ["Cz"], [], null, null, [8, 13]);

    private static TopomapResponse Response(params TopomapField[] fields) => new(
        "window", "recording", 10, 20, "unavailable", "psd-v1", "build",
        "montage_position_snapshot", "mri", "m", fields, null, null);
}

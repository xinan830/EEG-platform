using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class ScreenCalibrationTests
{
    [Fact]
    public void Store_RoundTripsCalibrationByDisplayKey()
    {
        var path = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"), "screen.json");
        var store = new ScreenCalibrationStore(path);
        var expected = new ScreenCalibrationProfile("display-a", 53.1, 29.9, DateTimeOffset.UtcNow);

        store.Save(expected);

        var actual = store.Load("display-a");
        Assert.NotNull(actual);
        Assert.Equal(expected.WidthCentimeters, actual.WidthCentimeters);
        Assert.Equal(expected.HeightCentimeters, actual.HeightCentimeters);
    }

    [Fact]
    public void ResolveScale_UsesIndependentPhysicalWidthAndHeightForOneDisplay()
    {
        var scale = new ScreenScaleContext("display-a", 25.4 / 96, 28.6 / 96);

        Assert.Equal(25.4 / 96, scale.MillimetersPerDipX, precision: 10);
        Assert.Equal(28.6 / 96, scale.MillimetersPerDipY, precision: 10);
        Assert.Equal(96 / 25.4, scale.DipsPerMillimeterX, precision: 10);
        Assert.Equal(96 / 28.6, scale.DipsPerMillimeterY, precision: 10);
    }

    [Fact]
    public void DraftWidth_UsesTheSamePhysicalConversionAsTheRuler()
    {
        var workspace = new ScreenCalibrationViewModel(
            new ScreenCalibrationStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "screen.json")),
            screenWidthDips: 1645.71);

        workspace.WidthCentimetersText = "34.3";

        Assert.Equal(0.2084, workspace.PreviewMillimetersPerDip, precision: 4);
        Assert.Equal(479.8, workspace.HorizontalRulerWidthDips, precision: 1);
        Assert.Equal(497.8, workspace.HorizontalRulerOuterWidthDips, precision: 1);
        Assert.Equal("0.2084 mm/DIP", workspace.MillimetersPerDipText);
    }

    [Fact]
    public void VerticalScale_UsesHeightCalibrationInsteadOfWidthCalibration()
    {
        var scale = ScreenScaleCalculator.VerticalDisplayScale(10, 1_000, 10, 0.25);

        Assert.Equal(0.004, scale, precision: 12);
    }
}

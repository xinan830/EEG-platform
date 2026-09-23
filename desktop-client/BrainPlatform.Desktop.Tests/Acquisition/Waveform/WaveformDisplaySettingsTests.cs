using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Tests.Views;

public sealed class WaveformDisplaySettingsTests
{
    [Fact]
    public void PaperSpeedAndTimebaseShareTheSameDisplayGeometry()
    {
        var settings = new WaveformDisplaySettings
        {
            ScreenScale = new ScreenScaleContext("test", 0.5, 0.25),
            ViewportWidthDips = 960,
            PaperSpeedMillimetersPerSecond = 30,
        };

        Assert.Equal(16, settings.GetVisibleSeconds(960), precision: 12);

        settings.TimebaseSecondsPerScreen = 5;
        settings.HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;

        Assert.Equal(5, settings.GetVisibleSeconds(320), precision: 12);
        Assert.Equal(96, settings.DerivedPaperSpeedMillimetersPerSecond, precision: 12);
    }

    [Fact]
    public void SettingsRejectUnsupportedClinicalDisplayValues()
    {
        var settings = new WaveformDisplaySettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.PaperSpeedMillimetersPerSecond = 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.TimebaseSecondsPerScreen = 7);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SensitivityMicrovoltsPerMillimeter = 0);
    }
}

using BrainPlatform.Desktop.ViewModels;
using System.Globalization;

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
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void Store_LoadLatestValidUsesMostRecentlyUpdatedProfile()
    {
        var path = CreateCalibrationPath();
        var store = new ScreenCalibrationStore(path);
        store.Save(new ScreenCalibrationProfile("display-a", 40, 25, DateTimeOffset.UtcNow.AddMinutes(-1)));
        store.Save(new ScreenCalibrationProfile("display-b", 53.1, 29.9, DateTimeOffset.UtcNow));

        var actual = store.LoadLatestValid();

        Assert.NotNull(actual);
        Assert.Equal("display-b", actual.DisplayKey);
        Assert.Equal(53.1, actual.WidthCentimeters);
        Assert.Equal(29.9, actual.HeightCentimeters);
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

    [Fact]
    public async Task ViewModel_ExplicitSaveRestoresInputsInANewInstance()
    {
        var path = CreateCalibrationPath();
        var first = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600);
        first.WidthCentimetersText = "34.3";
        first.HeightCentimetersText = "21.5";

        await first.SaveAsync();

        var restored = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600);
        Assert.Equal(34.3, Parse(restored.WidthCentimetersText));
        Assert.Equal(21.5, Parse(restored.HeightCentimetersText));
        Assert.True(restored.IsConfigured);
    }

    [Fact]
    public async Task ViewModel_ValidDraftAutoPersistsWithoutExplicitSave()
    {
        var path = CreateCalibrationPath();
        var first = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600)
        {
            WidthCentimetersText = "42.5",
            HeightCentimetersText = "24.8",
        };

        await Task.Delay(500);

        var restored = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600);
        Assert.Equal(42.5, Parse(restored.WidthCentimetersText));
        Assert.Equal(24.8, Parse(restored.HeightCentimetersText));
    }

    [Fact]
    public void ViewModel_LeavingPageFlushesAValidPendingDraft()
    {
        var path = CreateCalibrationPath();
        var first = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600)
        {
            WidthCentimetersText = "39.6",
            HeightCentimetersText = "22.1",
        };

        first.PersistValidDraftBeforeLeaving();

        var restored = new ScreenCalibrationViewModel(new ScreenCalibrationStore(path), screenWidthDips: 1_600);
        Assert.Equal(39.6, Parse(restored.WidthCentimetersText));
        Assert.Equal(22.1, Parse(restored.HeightCentimetersText));
    }

    [Fact]
    public async Task ViewModel_InvalidDraftDoesNotReplaceLastValidProfile()
    {
        var path = CreateCalibrationPath();
        var store = new ScreenCalibrationStore(path);
        store.Save(new ScreenCalibrationProfile("primary-display", 34.3, 21.5, DateTimeOffset.UtcNow));
        var workspace = new ScreenCalibrationViewModel(store, screenWidthDips: 1_600)
        {
            WidthCentimetersText = "2",
            HeightCentimetersText = "20",
        };

        await Task.Delay(500);

        var persisted = store.Load("primary-display");
        Assert.NotNull(persisted);
        Assert.Equal(34.3, persisted.WidthCentimeters);
        Assert.Equal(21.5, persisted.HeightCentimeters);
    }

    [Fact]
    public void ViewModel_DisplayContextReloadNotifiesBothInputBindings()
    {
        var path = CreateCalibrationPath();
        var store = new ScreenCalibrationStore(path);
        store.Save(new ScreenCalibrationProfile("primary-display", 34.3, 21.5, DateTimeOffset.UtcNow.AddMinutes(-1)));
        store.Save(new ScreenCalibrationProfile("display-b", 50.2, 28.4, DateTimeOffset.UtcNow));
        var workspace = new ScreenCalibrationViewModel(store, screenWidthDips: 1_600);
        var changed = new List<string?>();
        workspace.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        workspace.UpdateDisplayContext(new ScreenDisplayMetrics("display-b", 2_560, 1_440, 1.5, 1.5));

        Assert.Equal(50.2, Parse(workspace.WidthCentimetersText));
        Assert.Equal(28.4, Parse(workspace.HeightCentimetersText));
        Assert.Contains(nameof(ScreenCalibrationViewModel.WidthCentimetersText), changed);
        Assert.Contains(nameof(ScreenCalibrationViewModel.HeightCentimetersText), changed);
    }

    private static string CreateCalibrationPath() =>
        Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"), "screen.json");

    private static double Parse(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.CurrentCulture);
}

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Acquisition;

[Collection("WPF application resources")]
public sealed class ImpedancePresentationTests(WpfApplicationFixture application)
{
    [Theory]
    [InlineData(0, "#16A34A")]
    [InlineData(5, "#FACC15")]
    [InlineData(10, "#DC2626")]
    [InlineData(11, "#DC2626")]
    public void ScaleAnchorsAndSaturationAreExplicit(double value, string expected) =>
        Assert.Equal(expected, ImpedanceColorScale.Create(0, 5, 10)!.ColorAt(value));

    [Fact]
    public void InvalidThresholdsAndStaleReadingsStayNeutral()
    {
        Assert.Null(ImpedanceColorScale.Create(0, 10, 5));
        Assert.Null(ImpedanceColorScale.Create(0, double.NaN, 10));
        var row = new ImpedanceElectrodeRow(new(2, AcquisitionChannelKind.Reference), "Fz", null);
        row.Update(new(0, 2, AcquisitionChannelKind.Reference, "Fz", 2500, ImpedanceReadingStatus.Valid));
        Assert.Equal((2.5).ToString("0.0"), row.ValueText);
        row.SetPresentation(null, false);
        Assert.Equal("#64748B", row.Color);
        row.SetPresentation(ImpedanceColorScale.Create(0, 5, 10), true);
        Assert.Equal("#94A3B8", row.Color);
        Assert.Contains("过期", row.StatusText);
    }

    [Fact]
    public void GradientUsesContinuousRgbAndSharesNonuniformLegendAnchors()
    {
        var scale = ImpedanceColorScale.Create(10, 20, 50, "#000000", "#646464", "#C8C8C8")!;
        Assert.Equal("#323232", scale.ColorAt(15));
        Assert.Equal("#969696", scale.ColorAt(35));
        Assert.Equal("#000000", scale.ColorAt(5));
        Assert.Equal(0.25, scale.MiddleOffset);
        Assert.Equal(scale.MiddleOffset, scale.Brush.GradientStops[1].Offset);
        Assert.True(scale.Brush.IsFrozen);
        Assert.Null(ImpedanceColorScale.Create(0, 5, 10, "bad"));
        Assert.Null(ImpedanceColorScale.Create(-1, 5, 10));
        Assert.Null(ImpedanceColorScale.Create(0, 0, 10));
        Assert.Null(ImpedanceColorScale.Create(0, 5, double.PositiveInfinity));
        Assert.Equal("#94A3B8", scale.ColorAt(double.NaN));
    }

    [Fact]
    public void MissingAndInvalidMeasurementsNeverAcquireGradientColors()
    {
        var row = new ImpedanceElectrodeRow(new(2, AcquisitionChannelKind.Reference), "Fz", null);
        row.SetPresentation(ImpedanceColorScale.Create(0, 5, 10), false);
        Assert.Equal("#94A3B8", row.Color);
        row.Update(new(0, 2, AcquisitionChannelKind.Reference, "Fz", null, ImpedanceReadingStatus.Invalid));
        Assert.Equal("—", row.ValueText);
        Assert.Equal("#94A3B8", row.Color);
        Assert.Contains("无效", row.StatusText);
    }

    [Fact]
    public void PhysicalMapUsesRoleAndIndex_AndNeverInventsRefGroundCoordinates()
    {
        var physical = Physical();
        var input = ImpedanceElectrodeRow.Create(new(0, 4, AcquisitionChannelKind.Reference, "Wrong", 2500, ImpedanceReadingStatus.Valid), physical);
        Assert.Equal("Fz", input.Label);
        Assert.Equal(new ElectrodeMapPosition(0, -0.3), input.Position);
        var reference = ImpedanceElectrodeRow.Create(new(1, 4, AcquisitionChannelKind.ImpedanceReference, null, 2500, ImpedanceReadingStatus.Valid), physical);
        Assert.Equal("REF", reference.Label);
        Assert.Null(reference.Position);
        var unknown = ImpedanceElectrodeRow.Create(new(2, 8, AcquisitionChannelKind.Reference, null, null, ImpedanceReadingStatus.Missing), physical);
        Assert.Null(unknown.Position);
    }

    [Fact]
    public void FormerDefaultReferenceUsesSameNonoverlappingDisplaySiteWithoutRewritingProfile()
    {
        var physical = Physical() with { ReferenceMapPosition = new(0.0036, 0.4514) };
        var reading = new ImpedanceReading(1, 4, AcquisitionChannelKind.ImpedanceReference, null, 2500, ImpedanceReadingStatus.Valid);
        Assert.Equal(ElectrodeMapDefaults.Reference, ImpedanceElectrodeRow.Create(reading, physical).Position);
        Assert.Equal(new ElectrodeMapPosition(0.0036, 0.4514), physical.ReferenceMapPosition);
        var custom = new ElectrodeMapPosition(-0.15, 0.55);
        Assert.Equal(custom, ImpedanceElectrodeRow.Create(reading, physical with { ReferenceMapPosition = custom }).Position);
    }

    [Fact]
    public void WindowLoadsAndMapClickSharesSelectionWithList() => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var window = new AcquisitionImpedanceWindow(model, runtime, () => Task.CompletedTask, null);
        Assert.Same(model, window.DataContext);
        foreach (var item in new[] { ("Fz", 0d, -0.6), ("C3", -0.55, 0d), ("Cz", 0d, 0d), ("C4", 0.55, 0d), ("Pz", 0d, 0.6) })
        {
            var electrode = new ImpedanceElectrodeRow(new(model.Rows.Count, AcquisitionChannelKind.Reference), item.Item1, new(item.Item2, item.Item3));
            electrode.Update(new(model.Rows.Count, model.Rows.Count, AcquisitionChannelKind.Reference, item.Item1, 2500 + model.Rows.Count * 1500, ImpedanceReadingStatus.Valid));
            electrode.SetPresentation(ImpedanceColorScale.Create(0, 5, 10), false);
            model.Rows.Add(electrode);
        }
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(980, 630));
        content.Arrange(new Rect(0, 0, 980, 630));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(980, 630, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var image = File.Create(Path.Combine(Path.GetTempPath(), "eeg-impedance-window.png"))) encoder.Save(image);
        var row = new ImpedanceElectrodeRow(new(4, AcquisitionChannelKind.Reference), "Fz", new(0, -0.3));
        var map = new ImpedanceScalpMap { Rows = new ObservableCollection<ImpedanceElectrodeRow> { row } };
        map.Measure(new Size(400, 400));
        map.Arrange(new Rect(0, 0, 400, 400));
        map.UpdateLayout();
        var button = Assert.Single(((Canvas)map.Children[1]).Children.OfType<Button>());
        var marker = Assert.IsType<ElectrodeMarker>(button);
        Assert.Equal("Fz", marker.Label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(row, map.SelectedRow);
        Assert.True(marker.IsSelected);
        row.Update(new(0, 4, AcquisitionChannelKind.Reference, "Fz", 12345, ImpedanceReadingStatus.Valid));
        Assert.Equal((12.345).ToString("0.0"), marker.Reading);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    [Theory]
    [InlineData(1120, 860)]
    [InlineData(900, 660)]
    public void DenseMapUsesConfiguredPositionsAndRendersValidScale(int width, int height) => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        model.MiddleText = "20";
        model.MaximumText = "100";
        var window = new AcquisitionImpedanceWindow(model, runtime, () => Task.CompletedTask, null);
        // Synthetic positions exercise dense layout only; they are never acquisition defaults.
        for (var i = 0; i < 32; i++)
        {
            var angle = i * Math.PI * (3 - Math.Sqrt(5));
            var radius = 0.9 * Math.Sqrt((i + 0.5) / 32);
            var row = new ImpedanceElectrodeRow(new(i, AcquisitionChannelKind.Reference), $"E{i + 1}",
                new(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            row.Update(new(i, i, AcquisitionChannelKind.Reference, row.Label, i * 4000, ImpedanceReadingStatus.Valid));
            row.SetPresentation(model.ColorScale, false);
            model.Rows.Add(row);
        }
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var map = Descendants(content).OfType<ImpedanceScalpMap>().Single();
        Assert.True(map.ActualWidth >= 480);
        Assert.True(map.ActualHeight >= 480);
        var bounds = ScalpMapRenderer.GetHeadBounds(map.ActualWidth, map.ActualHeight, 5, 24);
        var markers = Descendants(map).OfType<ElectrodeMarker>().ToArray();
        Assert.Equal(32, markers.Length);
        Assert.All(markers, marker =>
        {
            var row = Assert.IsType<ImpedanceElectrodeRow>(marker.Tag);
            var expected = ScalpMapRenderer.ToCanvas(row.Position!.X, row.Position.Y, bounds);
            var diameter = ElectrodeMarker.Diameter(bounds);
            Assert.Equal(expected.X, Canvas.GetLeft(marker) + marker.Width / 2, 8);
            Assert.Equal(expected.Y, Canvas.GetTop(marker) + 20 + diameter / 2, 8);
            Assert.Equal(row.Label, marker.Label);
            Assert.Equal(row.ValueText, marker.Reading);
            Assert.True(Canvas.GetLeft(marker) >= 0);
            Assert.True(Canvas.GetLeft(marker) + marker.Width <= map.ActualWidth);
            Assert.True(Canvas.GetTop(marker) >= 0);
            Assert.True(Canvas.GetTop(marker) + marker.Height <= map.ActualHeight);
        });
        var legend = Assert.IsType<ImpedanceScaleLegend>(window.FindName("ColorLegend"));
        Assert.Same(model.ColorScale, legend.Scale);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Path.GetTempPath(), $"eeg-impedance-dense-{width}.png"));
        encoder.Save(output);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void ColorScaleEditorUpdatesLegendAndReadingsThroughOneMapping() => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        Assert.Null(model.ColorScale);
        model.MiddleText = "20";
        model.MaximumText = "100";
        Assert.Equal(0.2, model.ColorScale!.MiddleOffset);
        model.MiddleColor = "#112233";
        Assert.Equal("#112233", model.ColorScale!.ColorAt(20));
        Assert.Contains("100", model.LegendText);
        model.MaximumText = "invalid";
        Assert.Null(model.ColorScale);
        Assert.Contains("设置色标", model.LegendText);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    private static ChannelConfigurationProfile Physical() => new("physical", "Physical", "", ChannelConfigurationSource.User, "fake",
        [new(4, AcquisitionChannelKind.Reference, "Fz", false, 0, ManualMapPosition: new(0, -0.3))], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}

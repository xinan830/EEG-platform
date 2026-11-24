using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Acquisition;

[Collection("WPF application resources")]
public sealed class ImpedancePresentationTests(WpfApplicationFixture application)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosingWindowResumesOnlyWhenItInterruptedPreview(bool resumePreview) => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), Dispatcher.CurrentDispatcher);
        var resumed = false;
        var window = new AcquisitionImpedanceWindow(model, runtime, () =>
        {
            Assert.Equal(AcquisitionDeviceMode.Idle, runtime.DeviceMode.Mode);
            resumed = true;
            return Task.CompletedTask;
        }, null, resumePreviewOnClose: resumePreview);
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
        window.Closed += (_, _) => frame.Continue = false;
        window.Show();
        window.Close();
        timeout.Start();
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        Assert.Equal(resumePreview, resumed);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

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
    public void GradientUsesContinuousRgbAcrossConfiguredAnchors()
    {
        var scale = ImpedanceColorScale.Create(10, 20, 50, "#000000", "#646464", "#C8C8C8")!;
        Assert.Equal("#323232", scale.ColorAt(15));
        Assert.Equal("#969696", scale.ColorAt(35));
        Assert.Equal("#000000", scale.ColorAt(5));
        Assert.Equal("#646464", scale.ColorAt(20));
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
    public void WindowLoadsAsMapAndGradientEditorsAndMapClickHighlightsElectrode() => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var window = new AcquisitionImpedanceWindow(model, runtime, () => Task.CompletedTask, null);
        Assert.Same(model, window.DataContext);
        Assert.NotNull(model.ColorScale);
        Assert.Equal((0, 10, 20), (model.ColorScale.Minimum, model.ColorScale.Middle, model.ColorScale.Maximum));
        Assert.Equal(3, Descendants((FrameworkElement)window.Content).OfType<BrainPlatform.Desktop.Shared.Controls.ColorPicker>().Count());
        Assert.DoesNotContain(Descendants((FrameworkElement)window.Content).OfType<TextBlock>(), text =>
            text.Text.Contains("EEG 已停止", StringComparison.Ordinal) || text.Text.Contains("ANT/eego", StringComparison.Ordinal));
        foreach (var item in new[] { ("Fz", 0d, -0.6), ("C3", -0.55, 0d), ("Cz", 0d, 0d), ("C4", 0.55, 0d), ("Pz", 0d, 0.6) })
        {
            var electrode = new ImpedanceElectrodeRow(new(model.Rows.Count, AcquisitionChannelKind.Reference), item.Item1, new(item.Item2, item.Item3));
            electrode.Update(new(model.Rows.Count, model.Rows.Count, AcquisitionChannelKind.Reference, item.Item1, 2500 + model.Rows.Count * 1500, ImpedanceReadingStatus.Valid));
            electrode.SetPresentation(ImpedanceColorScale.Create(0, 5, 10), false);
            model.Rows.Add(electrode);
        }
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(980, 840));
        content.Arrange(new Rect(0, 0, 980, 840));
        content.UpdateLayout();
        var slider = Assert.Single(Descendants(content).OfType<Slider>());
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(200, slider.Maximum);
        Assert.Equal(20, slider.Value);
        slider.Value = 200;
        Assert.Equal(200, model.ColorScale!.Maximum);
        Assert.Same(model.ColorScale.Brush, slider.Background);
        slider.Value = 20;
        model.MiddleColor = "#0000FF";
        Assert.Equal("#0000FF", model.ColorScale!.ColorAt(10));
        var bitmap = new RenderTargetBitmap(980, 840, 96, 96, PixelFormats.Pbgra32);
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
        var bounds = ScalpMapRenderer.GetHeadBounds(map.ActualWidth, map.ActualHeight, 5, 24, 0.48);
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
        Assert.Equal(20, model.ColorScale!.Maximum);
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
    public void ColorEditorsAndGradientSliderUpdateTheSameMapping() => application.Run(() =>
    {
        var runtime = new ConfiguredAcquisitionRuntime();
        using var model = new AcquisitionImpedanceViewModel(runtime, Physical(), Guid.NewGuid(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
        Assert.NotNull(model.ColorScale);
        Assert.Equal((0, 10, 20), (model.ColorScale!.Minimum, model.ColorScale.Middle, model.ColorScale.Maximum));
        model.ColorScaleMaximumKiloOhms = 200;
        model.LowColor = "#0000FF";
        model.MiddleColor = "#FFFFFF";
        model.HighColor = "#000000";
        Assert.Equal("#0000FF", model.ColorScale!.ColorAt(0));
        Assert.Equal("#FFFFFF", model.ColorScale.ColorAt(100));
        Assert.Equal("#000000", model.ColorScale.ColorAt(200));
        Assert.Equal("#808080", model.ColorScale.ColorAt(150));
        Assert.Equal("#000000", model.ColorScale.ColorAt(250));
        model.ColorScaleMaximumKiloOhms = 5;
        Assert.Equal(5, model.ColorScale!.Maximum);
        model.ColorScaleMaximumKiloOhms = 0;
        Assert.Equal(0, model.ColorScale!.Maximum);
        Assert.Equal("#0000FF", model.ColorScale.ColorAt(0));
        Assert.Equal("#000000", model.ColorScale.ColorAt(0.1));
        model.ColorScaleMaximumKiloOhms = 1000;
        Assert.Equal(200, model.ColorScale!.Maximum);
        Assert.True(model.ColorScale.Brush.IsFrozen);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    private static ChannelConfigurationProfile Physical() => new("physical", "Physical", "", ChannelConfigurationSource.User, "fake",
        [new(4, AcquisitionChannelKind.Reference, "Fz", false, 0, ManualMapPosition: new(0, -0.3))], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}

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
    [InlineData(5, "#16A34A")]
    [InlineData(10, "#D97706")]
    [InlineData(11, "#DC2626")]
    public void ThresholdBoundariesAreInclusive(double value, string expected) =>
        Assert.Equal(expected, ImpedanceElectrodeRow.ThresholdColor(value, 5, 10));

    [Fact]
    public void InvalidThresholdsAndStaleReadingsStayNeutral()
    {
        Assert.Equal("#64748B", ImpedanceElectrodeRow.ThresholdColor(5, 10, 5));
        Assert.Equal("#64748B", ImpedanceElectrodeRow.ThresholdColor(5, double.NaN, 10));
        var row = new ImpedanceElectrodeRow(new(2, AcquisitionChannelKind.Reference), "Fz", null);
        row.Update(new(0, 2, AcquisitionChannelKind.Reference, "Fz", 2500, ImpedanceReadingStatus.Valid));
        Assert.Equal((2.5).ToString("0.0"), row.ValueText);
        row.SetPresentation(5, 10, true);
        Assert.Equal("#94A3B8", row.Color);
        Assert.Contains("过期", row.StatusText);
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
            electrode.SetPresentation(5, 10, false);
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
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(row, map.SelectedRow);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    });

    private static ChannelConfigurationProfile Physical() => new("physical", "Physical", "", ChannelConfigurationSource.User, "fake",
        [new(4, AcquisitionChannelKind.Reference, "Fz", false, 0, ManualMapPosition: new(0, -0.3))], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}

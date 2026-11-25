using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.ViewModels;
using BrainPlatform.Desktop.Modules.Channels.Views;
using BrainPlatform.Desktop.Shared.Controls;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Channels;

[Collection("WPF application resources")]
public sealed class ElectrodePositionWindowTests(WpfApplicationFixture application)
{
    [Fact]
    public void WindowStartsMaximizedAndKeepsTitleAndInstructions() => application.Run(() =>
    {
        var window = new ElectrodePositionWindow();
        Assert.Equal(WindowState.Maximized, window.WindowState);
        var texts = Descendants((FrameworkElement)window.Content).OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.Contains("电极位置", texts);
        Assert.Contains("仅列出已启用的 EEG 通道。选择通道后可在 2D 图中调整位置；REF 与 GND 标记可单独移动。", texts);
        window.Close();
    });

    [Theory]
    [InlineData(820, 620)]
    [InlineData(1536, 864)]
    public void WindowRendersElectrodeLayoutWithoutRefPzOverlap(int width, int height) => application.Run(() =>
    {
        var configuration = new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel());
        // Standard template positions are a display-only fixture; no device is opened.
        foreach (var (label, x, y) in new[]
        {
            ("Fp1", -0.262, -0.873), ("Fp2", 0.267, -0.880),
            ("F7", -0.644, -0.517), ("F3", -0.333, -0.464), ("Fz", 0.002, -0.462),
            ("F4", 0.346, -0.473), ("F8", 0.669, -0.531),
            ("T3", -0.744, 0.015), ("C3", -0.385, -0.040), ("Cz", 0.001, -0.058),
            ("C4", 0.390, -0.040), ("T4", 0.748, 0.012),
            ("T5", -0.595, 0.480), ("P3", -0.327, 0.372),
            ("Pz", -0.0010119920125431852, 0.34783527717868273),
            ("P4", 0.338, 0.373), ("T6", 0.593, 0.481),
            ("O1", -0.227, 0.735), ("O2", 0.221, 0.735),
            ("M1", -0.856, 0.336), ("M2", 0.853, 0.346),
        })
            configuration.DraftRows.Add(new ChannelConfigurationDraftRow(configuration.DraftRows.Count,
                "EEG", "V", "µV", label, true, configuration.DraftRows.Count, true,
                new ElectrodeLocationSnapshot(label, 0, 0, 0, "mri", "m", "mne.colin27_1020", "1.13.2", x, y)));
        configuration.DraftHardwareLandmarks.ReferencePosition = new(0.0036, 0.4514);
        using var model = new ElectrodePositionWindowViewModel(configuration, null);
        var window = new ElectrodePositionWindow { DataContext = model };
        var content = (FrameworkElement)window.Content;
        var tabs = Descendants(content).OfType<RadioButton>().ToArray();
        Assert.Equal(new[] { "2D", "3D" }, tabs.Select(tab => tab.Content).Cast<string>());
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var map = Descendants(content).OfType<ElectrodePositionMap>().Single();
        Assert.True(model.IsTwoDimensional);
        Assert.False(map.IsTopographic);
        Assert.True(map.IsHitTestVisible);
        var reset = Descendants(content).OfType<Button>().Single(button => Equals(button.Content, "恢复默认位置"));
        Assert.True(reset.IsEnabled);
        Assert.True(map.ActualWidth > 400);
        var bounds = ElectrodePositionMap.GetHeadBounds(map.ActualWidth, map.ActualHeight);
        var points = ElectrodeMapProjection.ProjectWithHardwareLandmarks(map.Rows!, map.ReferencePosition, map.GroundPosition);
        var reference = points.Single(point => point.Label == "REF");
        var pz = points.Single(point => point.Label == "Pz");
        Assert.True((ScalpMapRenderer.ToCanvas(reference.X, reference.Y, bounds) -
            ScalpMapRenderer.ToCanvas(pz.X, pz.Y, bounds)).Length > ElectrodeMarker.Diameter(bounds) + 4);
        Assert.True(bounds.Top >= 25);
        Assert.True(bounds.Bottom <= map.ActualHeight - 20);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Path.GetTempPath(), $"eeg-electrode-position-{width}-2d.png"));
        encoder.Save(output);
        Assert.Equal(new ElectrodeMapPosition(0.0036, 0.4514), configuration.DraftHardwareLandmarks.ReferencePosition);
        window.Close();
    });

    [Theory]
    [InlineData(820, 620)]
    [InlineData(1536, 864)]
    public void EnlargedMapKeepsHeadOutlineAndPeripheralMarkersInsideCanvas(double width, double height)
    {
        var bounds = ElectrodePositionMap.GetHeadBounds(width, height);
        Assert.True(bounds.Left >= 20);
        Assert.True(bounds.Right <= width - 20);
        Assert.True(bounds.Top >= 25);
        Assert.True(bounds.Bottom <= height - 20);
    }

    private static void SelectTab(IEnumerable<RadioButton> tabs, string title)
    {
        var tab = tabs.Single(button => Equals(button.Content, title));
        var binding = tab.GetBindingExpression(RadioButton.IsCheckedProperty)!;
        binding.UpdateTarget();
        tab.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        binding.UpdateSource();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

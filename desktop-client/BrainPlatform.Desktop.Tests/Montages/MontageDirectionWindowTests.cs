using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Montages.Views;
using BrainPlatform.Desktop.Shared.Controls;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Montages;

[Collection("WPF application resources")]
public sealed class MontageDirectionWindowTests(WpfApplicationFixture application)
{
    [Theory]
    [InlineData(620, 560)]
    [InlineData(820, 720)]
    [InlineData(1536, 864)]
    public void WindowStartsMaximizedAndEnlargedHeadFitsAvailableSpace(int width, int height) => application.Run(() =>
    {
        var workspace = new MontageConfigurationWorkspaceViewModel(
            new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel()));
        var source = MontageAppearanceTests.Source();
        workspace.BeginSessionProfile(source with
        {
            Channels = source.Channels.Select((channel, index) => channel with
            {
                ManualMapPosition = new(-0.3 - index * 0.3, -0.3),
            }).ToArray(),
        });
        var window = new MontageDirectionWindow(workspace);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var map = (MontageDirectionMap)window.FindName("DirectionMap");
        var head = VisualTreeHelper.GetDrawing(map).Children.OfType<GeometryDrawing>()
            .First(drawing => drawing.Geometry is EllipseGeometry).Geometry.Bounds;
        var expected = ScalpMapRenderer.GetHeadBounds(map.ActualWidth, map.ActualHeight, 5, 24, 0.46);
        Assert.Equal(expected, head);
        Assert.True(head.Top >= 25);
        Assert.True(head.Left >= 10);
        Assert.True(head.Right <= map.ActualWidth - 10);
        Assert.True(head.Bottom <= map.ActualHeight - 10);
        Assert.NotEmpty(map.Arrows!);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Path.GetTempPath(), $"eeg-montage-direction-{width}.png"));
        encoder.Save(output);
        window.Close();
    });
}

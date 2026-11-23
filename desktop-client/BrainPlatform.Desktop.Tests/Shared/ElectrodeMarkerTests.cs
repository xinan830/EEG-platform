using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrainPlatform.Desktop.Tests.Shared;

[Collection("WPF application resources")]
public sealed class ElectrodeMarkerTests(WpfApplicationFixture application)
{
    [Fact]
    public void NamedElectrodeUsesCircleOutlineAndCenteredTextAcrossBothRenderPaths() => application.Run(() =>
    {
        var position = new Point(40, 40);
        var channelPixels = Render(context => ScalpMapRenderer.DrawElectrode(context, "Cz", position, false, false, 1, 32));
        var sharedPixels = Render(context => ElectrodeMarker.Draw(context, "Cz", position, 32,
            Brushes.White, new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1));
        Assert.Equal(channelPixels, sharedPixels);
        // The named circle has blue paint at its edge and dark label pixels near its center.
        Assert.True(HasPixel(channelPixels, 22, 36, 26, 44, (r, g, b) => b > 150 && b > r));
        Assert.True(HasPixel(channelPixels, 31, 31, 49, 49, (r, g, b) => r < 80 && g < 80 && b < 80));
    });

    [Fact]
    public void SelectionAndReadingDoNotMoveTheElectrodeCircle() => application.Run(() =>
    {
        var normal = Render(context => ElectrodeMarker.Draw(context, "Fz", new(40, 40), 32, Brushes.White, Brushes.Blue, 1));
        var selected = Render(context => ElectrodeMarker.Draw(context, "Fz", new(40, 40), 32, Brushes.White, Brushes.Blue, 1, "23.0", true));
        Assert.True(HasPixel(selected, 28, 4, 52, 22, (r, g, b) => r < 180 && g < 180 && b < 180));
        for (var y = 28; y < 52; y++)
            for (var x = 28; x < 52; x++)
                for (var c = 0; c < 4; c++) Assert.Equal(normal[(y * 80 + x) * 4 + c], selected[(y * 80 + x) * 4 + c]);
    });

    [Fact]
    public void SharedTopographicDotKeepsExistingAlgorithmDotPixels() => application.Run(() =>
    {
        var expected = Render(context => context.DrawEllipse(Brushes.Black, null, new(40, 40), 1.8, 1.8));
        var actual = Render(context => ScalpMapRenderer.DrawElectrodeDot(context, new(40, 40)));
        Assert.Equal(expected, actual);
    });

    private static byte[] Render(Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) draw(context);
        var bitmap = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[80 * 80 * 4];
        bitmap.CopyPixels(pixels, 80 * 4, 0);
        return pixels;
    }

    private static bool HasPixel(byte[] pixels, int left, int top, int right, int bottom, Func<byte, byte, byte, bool> matches)
    {
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var index = (y * 80 + x) * 4;
                if (pixels[index + 3] > 200 && matches(pixels[index + 2], pixels[index + 1], pixels[index])) return true;
            }
        return false;
    }
}

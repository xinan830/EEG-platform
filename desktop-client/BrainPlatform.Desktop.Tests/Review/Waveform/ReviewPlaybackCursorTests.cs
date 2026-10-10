using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewPlaybackCursorTests
{
    [Theory]
    [InlineData(320, 0)]
    [InlineData(320, 0.5)]
    [InlineData(320, 1)]
    [InlineData(1000, 0)]
    [InlineData(1000, 0.5)]
    [InlineData(1000, 1)]
    public void Dashed_neutral_cursor_and_marker_stay_inside_plot(int width, double fraction)
    {
        ReviewTimelineNavigatorTests.OnSta(() =>
        {
            var plot = new Rect(40, 16, width - 60, 160);
            var cursor = new ReviewPlaybackCursor { Visibility = Visibility.Visible };
            cursor.SetPosition(plot, fraction);
            Assert.False(cursor.IsHitTestVisible);
            var host = new Grid { Background = Brushes.White };
            host.Children.Add(cursor);
            host.Measure(new Size(width, 200));
            host.Arrange(new Rect(0, 0, width, 200));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            SavePreview(bitmap, $"review-cursor-{width}-{fraction:0.0}.png");
            var pixels = new byte[width * 200 * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            bool IsDark(int x, int y) => pixels[(y * width + x) * 4 + 2] < 180;
            var darkPixels = new List<Point>();
            for (var y = 0; y < 200; y++)
            for (var x = 0; x < width; x++)
                if (IsDark(x, y)) darkPixels.Add(new Point(x, y));
            Assert.NotEmpty(darkPixels);
            Assert.All(darkPixels, point => Assert.True(plot.Contains(point), $"Cursor pixel outside plot: {point}"));
            var expectedX = plot.Left + Math.Clamp(plot.Width * fraction, 0.5, plot.Width - 0.5);
            var body = darkPixels.Where(point => point.Y >= plot.Top + 10).ToArray();
            Assert.NotEmpty(body);
            Assert.All(body, point => Assert.InRange(Math.Abs(point.X + 0.5 - expectedX), 0, 1));
            // Both visible and blank rows are required, not a solid vertical line.
            Assert.InRange(body.Select(point => point.Y).Distinct().Count(), 20, 110);
            Assert.Contains(darkPixels, point => point.Y < plot.Top + 5 && Math.Abs(point.X + 0.5 - expectedX) >= 1);
            var sample = darkPixels.MinBy(point => pixels[((int)point.Y * width + (int)point.X) * 4 + 2]);
            var pixelOffset = ((int)sample.Y * width + (int)sample.X) * 4;
            Assert.InRange(pixels[pixelOffset + 2], 70, 110);
        });
    }

    private static void SavePreview(BitmapSource bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BRAIN_REVIEW_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }
}

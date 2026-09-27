using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views;

internal static class StftBitmapRenderer
{
    private static readonly (byte Red, byte Green, byte Blue)[] Stops =
    [
        (25, 51, 75), (40, 127, 146), (228, 189, 92), (217, 76, 68),
    ];

    public static BitmapSource Create(StftPreview preview)
    {
        var width = preview.TimesSeconds.Length;
        var height = preview.FrequenciesHz.Length;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var time = 0; time < width; time++)
        {
            for (var frequency = 0; frequency < height; frequency++)
            {
                var offset = (height - 1 - frequency) * stride + time * 4;
                var value = preview.PowerDb[time][frequency];
                if (value is not { } power)
                {
                    pixels[offset] = 0xE2;
                    pixels[offset + 1] = 0xE8;
                    pixels[offset + 2] = 0xF0;
                }
                else
                {
                    var normalized = preview.MaximumDb > preview.MinimumDb
                        ? Math.Clamp((power - preview.MinimumDb) / (preview.MaximumDb - preview.MinimumDb), 0, 1)
                        : 0.5;
                    var section = Math.Min((int)(normalized * (Stops.Length - 1)), Stops.Length - 2);
                    var fraction = normalized * (Stops.Length - 1) - section;
                    var first = Stops[section];
                    var second = Stops[section + 1];
                    pixels[offset] = Mix(first.Blue, second.Blue, fraction);
                    pixels[offset + 1] = Mix(first.Green, second.Green, fraction);
                    pixels[offset + 2] = Mix(first.Red, second.Red, fraction);
                }
                pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte Mix(byte start, byte end, double amount) => (byte)Math.Round(start + (end - start) * amount);
}

using System.Globalization;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Display;

/// <summary>Opaque RGB presentation colors, independent of business modules.</summary>
public static class RgbColor
{
    public static bool TryParse(string? value, out Color color)
    {
        color = Colors.Transparent;
        var text = value?.Trim();
        if (text is not { Length: 7 } || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string Normalize(string value) => TryParse(value, out var color)
        ? Format(color)
        : throw new InvalidOperationException("颜色必须为 #RRGGBB 格式，例如 #2563EB。");
}

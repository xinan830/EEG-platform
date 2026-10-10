using System.Windows.Media;

namespace BrainPlatform.Desktop.Shared.Waveform;

/// <summary>One fallback palette for live and review output presentation.</summary>
public static class WaveformTraceColors
{
    private static readonly string[] Palette =
        ["#2563EB", "#0284C7", "#16A34A", "#F59E0B", "#EF4444", "#9333EA", "#475569", "#0891B2"];

    public static string DefaultAt(int outputIndex) => Palette[Math.Max(0, outputIndex) % Palette.Length];

    public static string Resolve(string? explicitColor, int outputIndex) =>
        string.IsNullOrWhiteSpace(explicitColor) ? DefaultAt(outputIndex) : RgbColor.Normalize(explicitColor);

    public static Color ToColor(string? value, int outputIndex)
    {
        var text = string.IsNullOrWhiteSpace(value) ? DefaultAt(outputIndex) : value;
        if (!RgbColor.TryParse(text, out var color))
            throw new InvalidOperationException("波形颜色必须为 #RRGGBB 格式。");
        return color;
    }
}

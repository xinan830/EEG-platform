using System.Windows;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Presentation-only kΩ anchors used to map impedance readings to electrode colors.</summary>
public sealed record ImpedanceColorScale
{
    public const double DisplayMaximumKiloOhms = 200;

    private ImpedanceColorScale(double minimum, double middle, double maximum, Color low, Color mid, Color high)
    {
        Minimum = minimum;
        Middle = middle;
        Maximum = maximum;
        Low = low;
        Mid = mid;
        High = high;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation,
            GradientStops = maximum == 0
                ? [new(low, 0), new(high, 0.000001), new(high, 1)]
                : [new(low, 0), new(mid, middle / DisplayMaximumKiloOhms), new(high, maximum / DisplayMaximumKiloOhms), new(high, 1)],
        };
        brush.Freeze();
        Brush = brush;
    }

    public double Minimum { get; }
    public double Middle { get; }
    public double Maximum { get; }
    public Color Low { get; }
    public Color Mid { get; }
    public Color High { get; }
    public LinearGradientBrush Brush { get; }
    public static ImpedanceColorScale? Create(double minimum, double middle, double maximum,
        string low = "#16A34A", string mid = "#FACC15", string high = "#DC2626") =>
        !double.IsFinite(minimum) || !double.IsFinite(middle) || !double.IsFinite(maximum) ||
        minimum < 0 || (maximum == 0 ? minimum != 0 || middle != 0 : minimum >= middle || middle >= maximum) ||
        !RgbColor.TryParse(low, out var first) || !RgbColor.TryParse(mid, out var second) || !RgbColor.TryParse(high, out var third)
            ? null : new(minimum, middle, maximum, first, second, third);

    public string ColorAt(double kilohms)
    {
        if (!double.IsFinite(kilohms) || kilohms < 0) return "#94A3B8";
        if (Maximum == 0) return kilohms == 0 ? RgbColor.Format(Low) : RgbColor.Format(High);
        var (from, to, ratio) = kilohms <= Middle
            ? (Low, Mid, Math.Clamp((kilohms - Minimum) / (Middle - Minimum), 0, 1))
            : (Mid, High, Math.Clamp((kilohms - Middle) / (Maximum - Middle), 0, 1));
        byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * ratio);
        return RgbColor.Format(Color.FromRgb(Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B)));
    }
}

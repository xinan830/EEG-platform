using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciChart.Charting.Visuals.RenderableSeries;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;

/// <summary>Shared scientific colormap stops for the STFT heatmap and its vertical colorbar.</summary>
internal static class StftColorPalette
{
    private static readonly (Color Color, double Offset)[] Stops =
    [
        (Color.FromRgb(25, 51, 75), 0.0),
        (Color.FromRgb(40, 127, 146), 0.33),
        (Color.FromRgb(228, 189, 92), 0.67),
        (Color.FromRgb(217, 76, 68), 1.0),
    ];

    public static HeatmapColorPalette CreateHeatmapColorPalette()
    {
        var palette = new HeatmapColorPalette
        {
            Precision = 4,
            AllowsHighPrecision = true,
        };
        foreach (var (color, offset) in Stops)
            palette.GradientStops.Add(new GradientStop(color, offset));
        return palette;
    }

    public static LinearGradientBrush CreateVerticalBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
        };
        foreach (var (color, offset) in Stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Lightweight light-theme vertical colorbar with nice dB tick placement.</summary>
public sealed class StftColorBar : FrameworkElement
{
    private const double BarWidth = 12d;
    private static readonly LinearGradientBrush GradientBrush = StftColorPalette.CreateVerticalBrush();
    private static readonly Brush EmptyFillBrush = CreateFrozenBrush(Color.FromRgb(241, 245, 249));
    private static readonly Brush LabelBrush = CreateFrozenBrush(Color.FromRgb(71, 85, 105));
    private static readonly Pen BorderPen = CreateFrozenPen(Color.FromRgb(203, 213, 225));
    private static readonly Pen TickPen = CreateFrozenPen(Color.FromRgb(148, 163, 184));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(StftColorBar),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(StftColorBar),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TopInsetProperty = DependencyProperty.Register(
        nameof(TopInset), typeof(double), typeof(StftColorBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomInsetProperty = DependencyProperty.Register(
        nameof(BottomInset), typeof(double), typeof(StftColorBar),
        new FrameworkPropertyMetadata(28d, FrameworkPropertyMetadataOptions.AffectsRender));

    public StftColorBar()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double TopInset
    {
        get => (double)GetValue(TopInsetProperty);
        set => SetValue(TopInsetProperty, value);
    }

    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    internal static IReadOnlyList<double> ComputeTicks(double min, double max, double barHeight)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max < min)
            return [];
        if (Math.Abs(max - min) < 1e-9)
            return [min];

        var maxTicks = Math.Clamp((int)Math.Floor(barHeight / 28d) + 1, 2, 6);
        var targetTicks = Math.Clamp((int)Math.Round(barHeight / 52d) + 1, 2, maxTicks);
        var rawStep = (max - min) / Math.Max(1, targetTicks - 1);
        var baseExp = Math.Floor(Math.Log10(rawStep));
        ReadOnlySpan<double> multipliers = [1d, 2d, 2.5d, 5d, 10d];

        List<double>? bestTicks = null;
        var bestScore = int.MaxValue;

        for (var expOffset = -1; expOffset <= 1; expOffset++)
        {
            var scale = Math.Pow(10, baseExp + expOffset);
            foreach (var mult in multipliers)
            {
                var step = mult * scale;
                if (step <= 0 || !double.IsFinite(step))
                    continue;

                var tol = step * 1e-9;
                var firstIndex = Math.Ceiling((min - tol) / step);
                var lastIndex = Math.Floor((max + tol) / step);
                var countDouble = lastIndex - firstIndex + 1;
                if (countDouble < 2 || countDouble > maxTicks)
                    continue;

                var count = (int)countDouble;
                var score = Math.Abs(count - targetTicks);
                if (score < bestScore)
                {
                    var candidate = new List<double>(count);
                    for (var idx = firstIndex; idx <= lastIndex; idx += 1)
                    {
                        var tick = idx * step;
                        if (Math.Abs(tick) < step * 1e-12)
                            tick = 0d;
                        candidate.Add(tick);
                    }
                    bestTicks = candidate;
                    bestScore = score;
                }
            }
        }

        if (bestTicks is not null)
            return bestTicks;

        return maxTicks >= 3
            ? [min, (min + max) / 2d, max]
            : [min, max];
    }

    internal static string FormatTick(double value, double step)
    {
        if (!double.IsFinite(value))
            return string.Empty;
        if (Math.Abs(value) < 1e-12)
            value = 0d;

        var abs = Math.Abs(value);
        if (abs >= 10000d || (abs > 0d && abs < 0.01d))
        {
            var sci = value.ToString("G4", CultureInfo.InvariantCulture);
            return sci == "-0" ? "0" : sci;
        }

        string format;
        if (step >= 1d && Math.Abs(value - Math.Round(value)) < 1e-9)
            format = "0";
        else if (step >= 0.1d)
            format = "0.#";
        else if (step >= 0.01d)
            format = "0.##";
        else
            format = "G4";

        var text = value.ToString(format, CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var barTop = Math.Max(0d, TopInset);
        var barBottom = Math.Max(barTop + 16d, ActualHeight - Math.Max(0d, BottomInset));
        var barHeight = barBottom - barTop;
        if (ActualWidth < BarWidth + 4d || barHeight < 16d)
            return;

        var hasValidRange = double.IsFinite(Minimum) && double.IsFinite(Maximum) && Maximum >= Minimum;
        var barRect = new Rect(0.5, barTop + 0.5, BarWidth, Math.Max(1d, barHeight - 1d));
        drawingContext.DrawRoundedRectangle(hasValidRange ? GradientBrush : EmptyFillBrush, BorderPen, barRect, 2d, 2d);
        if (!hasValidRange)
            return;

        var ticks = ComputeTicks(Minimum, Maximum, barHeight);
        if (ticks.Count == 0)
            return;

        var step = ticks.Count >= 2 ? Math.Abs(ticks[1] - ticks[0]) : 1d;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var span = Maximum - Minimum;
        var drawnIntervals = new List<(double Top, double Bottom)>(ticks.Count);

        foreach (var value in ticks)
        {
            var t = Math.Abs(span) < 1e-9 ? 0.5d : Math.Clamp((value - Minimum) / span, 0d, 1d);
            var y = barBottom - t * barHeight;
            var snappedY = Math.Round(y) + 0.5d;
            drawingContext.DrawLine(TickPen, new Point(BarWidth + 1d, snappedY), new Point(BarWidth + 5d, snappedY));

            var text = new FormattedText(
                FormatTick(value, step),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10.5,
                LabelBrush,
                dpi);

            var labelY = Math.Clamp(y - text.Height / 2d, barTop, Math.Max(barTop, barBottom - text.Height));
            var labelTop = labelY;
            var labelBottom = labelY + text.Height;
            if (drawnIntervals.Any(existing => labelTop < existing.Bottom + 1d && labelBottom + 1d > existing.Top))
                continue;

            drawnIntervals.Add((labelTop, labelBottom));
            drawingContext.DrawText(text, new Point(BarWidth + 7d, labelY));
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color)
    {
        var pen = new Pen(CreateFrozenBrush(color), 1d);
        pen.Freeze();
        return pen;
    }
}

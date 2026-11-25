using System.Windows;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Compact vector sparkline rendering a normalized ICA component timecourse preview.</summary>
public sealed class IcSparklineControl : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(IcSparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(IcSparklineControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(37, 99, 235)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimesSecondsProperty = DependencyProperty.Register(
        nameof(TimesSeconds), typeof(IReadOnlyList<double>), typeof(IcSparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds), typeof(double), typeof(IcSparklineControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public IReadOnlyList<double>? TimesSeconds
    {
        get => (IReadOnlyList<double>?)GetValue(TimesSecondsProperty);
        set => SetValue(TimesSecondsProperty, value);
    }

    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1), new Rect(0, 0, w, h), 4, 4);

        var midY = h * 0.5;
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 0.75), new Point(2, midY), new Point(w - 2, midY));

        if (Samples is not { Count: >= 2 } data || TimesSeconds is not { } times ||
            times.Count != data.Count || !double.IsFinite(DurationSeconds) || DurationSeconds <= 0) return;
        var peak = 0.0;
        for (var i = 0; i < data.Count; i++)
        {
            var a = Math.Abs(data[i]);
            if (double.IsFinite(a) && a > peak) peak = a;
        }
        if (peak < 1e-9) peak = 1.0;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            var xScale = (w - 6) / DurationSeconds;
            var yScale = (h - 8) * 0.48 / peak;
            var started = false;
            for (var i = 0; i < data.Count; i++)
            {
                var v = data[i];
                if (!double.IsFinite(v)) { started = false; continue; }
                var pt = new Point(3 + times[i] * xScale, midY - v * yScale);
                if (!started) { ctx.BeginFigure(pt, false, false); started = true; }
                else ctx.LineTo(pt, true, false);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, new Pen(StrokeBrush, 1.15), geom);
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SciChart.Charting;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.PointMarkers;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

/// <summary>Shared light-theme styling, Y-axis range, and IAPF presentation helpers for live/review scalar charts.</summary>
internal static class ScalarTrendChartStyle
{
    internal const double IapfAxisMinHz = 8.0;
    internal const double IapfAxisMaxHz = 13.0;
    internal const double ClassicAlphaMinHz = 8.0;
    internal const double ClassicAlphaMaxHz = 13.0;
    internal const string IapfDisplayModeCog = "cog";
    internal const string IapfDisplayModeCompare = "compare";
    internal const string IapfDisplayModePeak = "peak";

    internal static readonly string[] PreferredIapfRoles = ["Oz", "O1", "O2", "Pz", "P3", "P4"];

    private static readonly Color MainStrokeColor = Color.FromRgb(22, 119, 155);
    private static readonly Color CogStrokeColor = Color.FromRgb(217, 119, 6);
    private static readonly Color TickBrushColor = Color.FromRgb(71, 85, 105);
    private static readonly Color GridLineColor = Color.FromRgb(226, 232, 240);

    internal static (bool AutoRange, double Min, double Max) ComputeYAxisRange(string? algorithmId) =>
        string.Equals(algorithmId, "iapf", StringComparison.OrdinalIgnoreCase)
            ? (false, IapfAxisMinHz, IapfAxisMaxHz)
            : (true, double.NaN, double.NaN);

    internal static string? SelectPreferredIapfChannel(
        IReadOnlyList<string> channels,
        Func<string, string?>? resolvePositiveRole = null)
    {
        if (channels.Count == 0)
            return null;

        foreach (var role in PreferredIapfRoles)
        {
            var match = channels.FirstOrDefault(channel =>
                string.Equals(channel, role, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, $"{role}-REF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, $"{role}-AVG", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        if (resolvePositiveRole is not null)
        {
            foreach (var role in PreferredIapfRoles)
            {
                var match = channels.FirstOrDefault(channel =>
                    string.Equals(resolvePositiveRole(channel), role, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match;
            }
        }

        return channels[0];
    }

    internal static string FormatDisplayUnit(string? unit) =>
        string.IsNullOrWhiteSpace(unit) || string.Equals(unit, "dimensionless", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : unit;

    internal static string NormalizeIapfDisplayMode(string? mode)
    {
        if (string.Equals(mode, IapfDisplayModeCompare, StringComparison.OrdinalIgnoreCase))
            return IapfDisplayModeCompare;
        if (string.Equals(mode, IapfDisplayModePeak, StringComparison.OrdinalIgnoreCase))
            return IapfDisplayModePeak;
        return IapfDisplayModeCog;
    }

    internal static string FormatIapfLatestValue(string? displayMode, double? valueHz, double? cogHz, string? unit)
    {
        var mode = NormalizeIapfDisplayMode(displayMode);
        if (mode is IapfDisplayModeCog or IapfDisplayModeCompare && cogHz is { } cog && double.IsFinite(cog))
            return $"COG {cog.ToString("0.##", CultureInfo.InvariantCulture)} Hz";
        if (valueHz is { } value && double.IsFinite(value))
        {
            var formatted = value.ToString("0.##", CultureInfo.InvariantCulture);
            var displayUnit = FormatDisplayUnit(unit);
            return string.IsNullOrEmpty(displayUnit) ? formatted : $"{formatted} {displayUnit}";
        }
        return "--";
    }

    internal static string? FormatIapfSummaryForMode(
        string? displayMode,
        string? source,
        double? valueHz,
        double? peakHz,
        double? cogHz,
        bool splitPeak = false)
    {
        var mode = NormalizeIapfDisplayMode(displayMode);
        if (mode == IapfDisplayModePeak)
            return FormatIapfSummary(source, peakHz, cogHz, splitPeak);
        if (source == "peak" && (valueHz ?? peakHz) is { } p && double.IsFinite(p))
            return $"综合 IAPF {p.ToString("0.##", CultureInfo.InvariantCulture)} Hz · 峰值估计";
        if (source == "cog" && splitPeak)
        {
            return peakHz is { } splitMain && double.IsFinite(splitMain)
                ? $"双峰回退重心 · 主峰 {splitMain.ToString("0.##", CultureInfo.InvariantCulture)} Hz"
                : "双峰回退重心";
        }
        if (source == "cog")
            return "重心估计";
        return FormatIapfSummary(source, peakHz, cogHz, splitPeak);
    }

    internal static string? FormatIapfSummary(string? source, double? peakHz, double? cogHz, bool splitPeak = false)
    {
        var cogText = cogHz is { } cog && double.IsFinite(cog)
            ? $"COG {cog.ToString("0.##", CultureInfo.InvariantCulture)} Hz"
            : null;
        return source switch
        {
            "peak" => cogText is not null ? $"峰值估计 · {cogText}" : "峰值估计",
            "cog" when splitPeak => peakHz is { } p && double.IsFinite(p)
                ? $"双峰回退重心 · 主峰 {p.ToString("0.##", CultureInfo.InvariantCulture)} Hz"
                : "双峰回退重心",
            "cog" => "重心估计",
            _ => cogText,
        };
    }

    internal static void ApplySurfaceChrome(SciChartSurface surface)
    {
        ThemeManager.SetTheme(surface, "BrightSpark");
        surface.Background = Brushes.White;
        surface.BorderThickness = new Thickness(0);
        surface.ShowLicensingWarnings = false;
    }

    internal static void ApplyAxisStyle(NumericAxis axis)
    {
        var tickBrush = new SolidColorBrush(TickBrushColor);
        tickBrush.Freeze();
        var gridBrush = new SolidColorBrush(GridLineColor);
        gridBrush.Freeze();
        var gridStyle = new Style(typeof(Line));
        gridStyle.Setters.Add(new Setter(Shape.StrokeProperty, gridBrush));
        gridStyle.Setters.Add(new Setter(Shape.StrokeThicknessProperty, 1d));

        axis.DrawMajorBands = false;
        axis.DrawMinorGridLines = false;
        axis.DrawMinorTicks = false;
        axis.FontSize = 11;
        axis.TickTextBrush = tickBrush;
        axis.MajorGridLineStyle = gridStyle;
    }

    internal static BoxAnnotation CreateClassicAlphaBand()
    {
        var background = new SolidColorBrush(Color.FromArgb(24, 22, 119, 155));
        background.Freeze();
        var border = new SolidColorBrush(Color.FromArgb(64, 22, 119, 155));
        border.Freeze();
        return new BoxAnnotation
        {
            CoordinateMode = AnnotationCoordinateMode.RelativeX,
            X1 = 0d,
            X2 = 1d,
            Y1 = ClassicAlphaMinHz,
            Y2 = ClassicAlphaMaxHz,
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(0, 1, 0, 1),
            AnnotationCanvas = AnnotationCanvas.BelowChart,
            IsEditable = false,
            IsHidden = true,
        };
    }

    internal static FastLineRenderableSeries CreateCogReferenceSeries(XyDataSeries<double, double> series) => new()
    {
        DataSeries = series,
        Stroke = Color.FromArgb(200, CogStrokeColor.R, CogStrokeColor.G, CogStrokeColor.B),
        StrokeThickness = 2,
        StrokeDashArray = [4, 3],
        DrawNaNAs = LineDrawMode.Gaps,
        IsVisible = false,
    };

    internal static FastLineRenderableSeries CreateMainLineSeries(XyDataSeries<double, double> series) => new()
    {
        DataSeries = series,
        Stroke = MainStrokeColor,
        StrokeThickness = 2,
        DrawNaNAs = LineDrawMode.Gaps,
    };

    internal static EllipsePointMarker CreateDefaultPointMarker() => new()
    {
        Width = 5.5,
        Height = 5.5,
        Fill = MainStrokeColor,
        Stroke = Colors.White,
        StrokeThickness = 1,
    };

    internal static void ApplyCogSeriesStyle(FastLineRenderableSeries series, bool isPrimary)
    {
        if (isPrimary)
        {
            series.Stroke = MainStrokeColor;
            series.StrokeThickness = 2;
            series.StrokeDashArray = null;
        }
        else
        {
            series.Stroke = Color.FromArgb(200, CogStrokeColor.R, CogStrokeColor.G, CogStrokeColor.B);
            series.StrokeThickness = 2;
            series.StrokeDashArray = [4, 3];
        }
    }

    internal static void ApplyIapfPeakScatterStyle(XyScatterRenderableSeries series, bool isCompareMode)
    {
        series.PointMarker = isCompareMode
            ? new EllipsePointMarker
            {
                Width = 6,
                Height = 6,
                Fill = Color.FromArgb(135, MainStrokeColor.R, MainStrokeColor.G, MainStrokeColor.B),
                Stroke = Color.FromArgb(190, MainStrokeColor.R, MainStrokeColor.G, MainStrokeColor.B),
                StrokeThickness = 1,
            }
            : new EllipsePointMarker
            {
                Width = 6.5,
                Height = 6.5,
                Fill = MainStrokeColor,
                Stroke = Colors.White,
                StrokeThickness = 1.2,
            };
        RolloverModifier.SetIncludeSeries(series, isCompareMode);
    }

    internal static XyScatterRenderableSeries CreateIapfPeakScatterSeries(XyDataSeries<double, double> series)
    {
        var renderable = new XyScatterRenderableSeries
        {
            DataSeries = series,
            IsVisible = false,
            PointMarker = new EllipsePointMarker
            {
                Width = 6.5,
                Height = 6.5,
                Fill = MainStrokeColor,
                Stroke = Colors.White,
                StrokeThickness = 1.2,
            },
        };
        RolloverModifier.SetIncludeSeries(renderable, false);
        return renderable;
    }

    internal static XyScatterRenderableSeries CreateIapfCogScatterSeries(XyDataSeries<double, double> series)
    {
        var renderable = new XyScatterRenderableSeries
        {
            DataSeries = series,
            IsVisible = false,
            PointMarker = new EllipsePointMarker
            {
                Width = 6.5,
                Height = 6.5,
                Fill = Colors.White,
                Stroke = CogStrokeColor,
                StrokeThickness = 1.8,
            },
        };
        RolloverModifier.SetIncludeSeries(renderable, false);
        return renderable;
    }

    internal static void RebuildSeries(
        XyDataSeries<double, double> series,
        IEnumerable<(double X, double Y)> points)
    {
        using var _ = series.SuspendUpdates();
        series.Clear();
        var lastX = double.NegativeInfinity;
        foreach (var (x, y) in points)
        {
            if (!double.IsFinite(x) || x <= lastX)
                continue;
            lastX = x;
            series.Append(x, y);
        }
    }
}

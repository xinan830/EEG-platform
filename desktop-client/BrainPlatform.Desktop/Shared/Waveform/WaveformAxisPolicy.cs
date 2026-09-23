using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Shared.Waveform;

/// <summary>
/// Shared axis defaults for stacked EEG waveform surfaces.
/// The caller owns the label provider and any mode-specific styling.
/// </summary>
internal static class WaveformAxisPolicy
{
    public static void ConfigureHorizontalAxis(NumericAxis axis, NumericLabelProvider labelProvider)
    {
        axis.AutoRange = AutoRange.Never;
        axis.VisibleRange = new DoubleRange(0, 10);
        axis.AxisAlignment = AxisAlignment.Bottom;
        axis.DrawLabels = true;
        axis.DrawMajorBands = false;
        axis.DrawMinorGridLines = false;
        axis.DrawMajorGridLines = true;
        axis.DrawMajorTicks = false;
        axis.DrawMinorTicks = false;
        axis.AutoTicks = false;
        axis.MajorDelta = 1d;
        axis.MinorDelta = 0.5d;
        axis.TextFormatting = "0";
        axis.LabelProvider = labelProvider;
    }

    public static void ConfigureStackedTraceAxis(NumericAxis axis)
    {
        axis.AutoRange = AutoRange.Never;
        axis.VisibleRange = new DoubleRange(0, 1);
        axis.AxisAlignment = AxisAlignment.Right;
        axis.DrawLabels = false;
        axis.DrawMajorBands = false;
        axis.DrawMinorGridLines = false;
        axis.DrawMajorGridLines = false;
        axis.DrawMajorTicks = false;
        axis.DrawMinorTicks = false;
    }
}

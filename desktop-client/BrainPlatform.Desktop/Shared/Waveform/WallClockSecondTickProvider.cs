using SciChart.Charting.Model;
using SciChart.Charting.Numerics.TickProviders;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Shared.Waveform;

internal sealed class WallClockSecondTickProvider : NumericTickProvider
{
    public DateTimeOffset? OriginUtc { get; set; }

    protected override IList<double> CalculateMajorTicks(IRange<double> tickRange, IAxisDelta<double> tickDelta)
    {
        if (OriginUtc is not { } origin || !double.IsFinite(tickRange.Min) || !double.IsFinite(tickRange.Max))
        {
            return base.CalculateMajorTicks(tickRange, tickDelta);
        }

        return CalculateAlignedSeconds(tickRange.Min, tickRange.Max, origin);
    }

    internal static IList<double> CalculateAlignedSeconds(double min, double max, DateTimeOffset origin)
    {
        var fractionalSecond = origin.UtcTicks % TimeSpan.TicksPerSecond / (double)TimeSpan.TicksPerSecond;
        var first = Math.Ceiling(min + fractionalSecond - 1e-9);
        var ticks = new List<double>();
        for (var second = first; second - fractionalSecond <= max + 1e-9; second++)
        {
            ticks.Add(second - fractionalSecond);
        }

        return ticks;
    }
}

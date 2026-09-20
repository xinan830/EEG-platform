using System.Globalization;
using SciChart.Charting.Visuals.Axes.LabelProviders;

namespace BrainPlatform.Desktop.Views;

/// <summary>Formats recording-relative x values as their recorded local clock time.</summary>
internal sealed class RecordingTimeLabelProvider : NumericLabelProvider
{
    private DateTimeOffset recordingStartUtc;
    private double windowStartSeconds;

    public void Update(DateTimeOffset startUtc, double windowStart)
    {
        recordingStartUtc = startUtc;
        windowStartSeconds = windowStart;
    }

    public override string FormatLabel(IComparable dataValue)
    {
        var relativeSeconds = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
        return recordingStartUtc
            .AddTicks(ToTicks(windowStartSeconds + relativeSeconds))
            .ToLocalTime()
            .ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
    }

    private static long ToTicks(double seconds) =>
        checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));
}

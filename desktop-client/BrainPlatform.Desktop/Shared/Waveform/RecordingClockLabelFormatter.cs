using System.Globalization;

namespace BrainPlatform.Desktop.Shared.Waveform;

internal static class RecordingClockLabelFormatter
{
    public static string Format(DateTimeOffset startUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return string.Empty;
        return startUtc.AddSeconds(elapsedSeconds).ToLocalTime()
            .ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public static string FormatAlignedSecond(DateTimeOffset startUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return string.Empty;
        var local = startUtc.AddSeconds(elapsedSeconds).ToLocalTime();
        var roundedTicks = (long)Math.Round(local.Ticks / (double)TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond;
        return new DateTimeOffset(roundedTicks, local.Offset).ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public static string FormatMilliseconds(DateTimeOffset startUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return string.Empty;
        return startUtc.AddSeconds(elapsedSeconds).ToLocalTime()
            .ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
    }
}

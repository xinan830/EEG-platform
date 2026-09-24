using System.Globalization;
using BrainPlatform.Desktop.Shared.Time;

namespace BrainPlatform.Desktop.Shared.Waveform;

internal static class RecordingClockLabelFormatter
{
    public static string Format(DateTimeOffset startUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return string.Empty;
        return RecordingTimeMapper.ToLocalTime(startUtc, elapsedSeconds)
            .ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public static string FormatAlignedSecond(DateTimeOffset startUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) return string.Empty;
        var local = RecordingTimeMapper.ToLocalTime(startUtc, elapsedSeconds);
        var roundedTicks = (long)Math.Round(local.Ticks / (double)TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond;
        return new DateTimeOffset(roundedTicks, local.Offset).ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public static string FormatSampleMilliseconds(DateTimeOffset startUtc, long sample, int samplingRateHz)
    {
        return RecordingTimeMapper.ToLocalTime(startUtc, sample, samplingRateHz)
            .ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
    }
}

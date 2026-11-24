using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Events.Domain;

public static class RecordingEventDisplayTime
{
    public static string FormatClock(DateTimeOffset recordingStartUtc, long startSample, int samplingRateHz)
    {
        var estimatedTime = EstimateLocalTime(recordingStartUtc, startSample, samplingRateHz);
        return $"约 {estimatedTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.CurrentCulture)}";
    }

    public static string FormatClockTime(DateTimeOffset recordingStartUtc, long startSample, int samplingRateHz) =>
        $"约 {EstimateLocalTime(recordingStartUtc, startSample, samplingRateHz).ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture)}";

    public static string FormatMarker(
        RecordingEvent item,
        DateTimeOffset recordingStartUtc,
        int samplingRateHz)
    {
        var clock = FormatClock(recordingStartUtc, item.StartSample, samplingRateHz);
        var elapsedSeconds = RecordingTimeMapper.ToElapsedSeconds(item.StartSample, samplingRateHz);
        return $"{item.DefinitionSnapshot.Name} · 记录时间 {clock}" +
               $"（记录后 {elapsedSeconds.ToString("0.###", CultureInfo.CurrentCulture)} 秒）";
    }

    private static DateTimeOffset EstimateLocalTime(DateTimeOffset recordingStartUtc, long startSample, int samplingRateHz)
    {
        return RecordingTimeMapper.ToLocalTime(recordingStartUtc, startSample, samplingRateHz);
    }
}

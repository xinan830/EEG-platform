using System.Globalization;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

/// <summary>Recording-elapsed UI time; sample rounding matches manual event creation.</summary>
public static class ReviewEventTime
{
    public static string Format(double seconds)
    {
        var milliseconds = checked((long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
        return string.Create(CultureInfo.InvariantCulture,
            $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}");
    }

    public static double Parse(string text)
    {
        var parts = text.Trim().Split(':', '.');
        if (parts.Length != 4 || parts[0].Length < 2 || parts[1].Length != 2 ||
            parts[2].Length != 2 || parts[3].Length != 3 ||
            !parts.All(part => part.All(char.IsAsciiDigit)) ||
            !long.TryParse(parts[0], out var hours) ||
            !int.TryParse(parts[1], out var minutes) || minutes > 59 ||
            !int.TryParse(parts[2], out var seconds) || seconds > 59 ||
            !int.TryParse(parts[3], out var milliseconds))
            throw new EventValidationException("event_time_format", "标记时间格式应为 时:分:秒.毫秒，例如 00:00:13.740。");
        return hours * 3600d + minutes * 60d + seconds + milliseconds / 1000d;
    }

    public static long ToSample(double seconds, int samplingRateHz, long sampleCount, long durationSamples = 0)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds >= sampleCount / (double)samplingRateHz)
            throw new EventValidationException("event_start_invalid", "标记时间必须在当前录音范围内。");
        var sample = checked((long)Math.Round(seconds * samplingRateHz, MidpointRounding.AwayFromZero));
        if (sample >= sampleCount || durationSamples < 0 || durationSamples > sampleCount - sample)
            throw new EventValidationException("event_duration_invalid", "标记移动后超出录音范围；区间时长保持不变。");
        return sample;
    }
}

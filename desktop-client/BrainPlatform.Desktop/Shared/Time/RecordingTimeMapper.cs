namespace BrainPlatform.Desktop.Shared.Time;

/// <summary>Maps immutable recording sample coordinates to estimated clock time.</summary>
public static class RecordingTimeMapper
{
    public static double ToElapsedSeconds(long sample, int samplingRateHz)
    {
        Validate(sample, samplingRateHz);
        return sample / (double)samplingRateHz;
    }

    public static DateTimeOffset ToLocalTime(DateTimeOffset recordingStartUtc, long sample, int samplingRateHz)
    {
        Validate(sample, samplingRateHz);
        var elapsedTicks = checked((long)decimal.Round(
            (decimal)sample * TimeSpan.TicksPerSecond / samplingRateHz,
            0,
            MidpointRounding.AwayFromZero));
        return recordingStartUtc.AddTicks(elapsedTicks).ToLocalTime();
    }

    public static DateTimeOffset ToLocalTime(DateTimeOffset recordingStartUtc, double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return recordingStartUtc.AddSeconds(elapsedSeconds).ToLocalTime();
    }

    private static void Validate(long sample, int samplingRateHz)
    {
        if (sample < 0) throw new ArgumentOutOfRangeException(nameof(sample));
        if (samplingRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(samplingRateHz));
    }
}

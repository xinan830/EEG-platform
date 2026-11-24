namespace BrainPlatform.Desktop.Shared.Time;

/// <summary>Maps immutable recording sample coordinates to estimated clock time.</summary>
public static class RecordingTimeMapper
{
    /// <summary>Maps a cursor boundary without forward rounding or closing recorded gaps.</summary>
    public static long ToCursorSample(double elapsedSeconds, int samplingRateHz, long durationSamples)
    {
        Validate(durationSamples, samplingRateHz);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 ||
            elapsedSeconds > durationSamples / (double)samplingRateHz)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        var product = elapsedSeconds * samplingRateHz;
        // long.MaxValue rounds to 2^63 as double; never cast that boundary silently.
        if (!double.IsFinite(product) || product >= 9223372036854775808d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        var sample = checked((long)Math.Floor(product));
        if (sample > durationSamples) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return sample;
    }

    public static long ToRelativeSample(long sampleCounter, long firstSampleCounter)
    {
        var sample = checked(sampleCounter - firstSampleCounter);
        if (sample < 0) throw new ArgumentOutOfRangeException(nameof(sampleCounter));
        return sample;
    }

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

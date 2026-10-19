namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>
/// The immutable geometry contract for a future disposable display-level cache.
/// It does not read samples or alter the current SciChart projection.
/// </summary>
public sealed record ReviewDisplayLevel(
    string SourceFingerprint,
    int SamplingRateHz,
    int ChannelCount,
    int PlotWidthPixels,
    double VisibleDurationSeconds,
    long SamplesPerBucket)
{
    public long MaximumPointsPerTrace => checked(Math.Min(
        2_000L,
        Math.Max(1L, (long)PlotWidthPixels * 2L)));

    public string Identity => string.Join("|",
        "review-display-level-v1",
        SourceFingerprint,
        SamplingRateHz,
        ChannelCount,
        PlotWidthPixels,
        VisibleDurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        SamplesPerBucket);
}

internal static class ReviewDisplayLevelSelector
{
    internal static ReviewDisplayLevel Select(
        double visibleDurationSeconds,
        int samplingRateHz,
        int plotWidthPixels,
        int channelCount,
        string sourceFingerprint)
    {
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplingRateHz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plotWidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        if (string.IsNullOrWhiteSpace(sourceFingerprint))
            throw new ArgumentException("Display source identity is required.", nameof(sourceFingerprint));

        var samples = checked((long)Math.Ceiling(visibleDurationSeconds * samplingRateHz));
        var pointBudget = Math.Min(2_000L, Math.Max(1L, (long)plotWidthPixels * 2L));
        var minimumBucket = Math.Max(1L, (samples + pointBudget - 1) / pointBudget);
        var bucket = NextPowerOfTwo(minimumBucket);
        return new ReviewDisplayLevel(sourceFingerprint, samplingRateHz, channelCount,
            plotWidthPixels, visibleDurationSeconds, bucket);
    }

    private static long NextPowerOfTwo(long value)
    {
        var result = 1L;
        while (result < value)
        {
            if (result > long.MaxValue / 2) return long.MaxValue;
            result *= 2;
        }
        return result;
    }
}

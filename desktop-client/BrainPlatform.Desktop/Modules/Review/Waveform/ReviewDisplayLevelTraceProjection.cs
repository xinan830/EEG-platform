using System;
using System.Collections.Generic;
using BrainPlatform.Desktop.Modules.Review.Cache;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ReviewDisplayTracePoints(
    IReadOnlyList<double> XSeconds,
    IReadOnlyList<double> ValuesVolts);

/// <summary>
/// Converts stored extrema into time/value points for a chart adapter. It does
/// not apply montage, filtering, scaling, or scientific calculations.
/// </summary>
internal static class ReviewDisplayLevelTraceProjection
{
    public static ReviewDisplayTracePoints Build(
        ReviewDisplayLevel level,
        IReadOnlyList<ReviewDisplayLevelChunk> chunks,
        double recordingStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(chunks);
        if (!double.IsFinite(recordingStartSeconds))
            throw new ArgumentOutOfRangeException(nameof(recordingStartSeconds));

        var x = new List<double>();
        var values = new List<double>();
        long? previousEnd = null;
        foreach (var chunk in chunks)
        {
            if (!string.Equals(chunk.LevelIdentity, level.Identity, StringComparison.Ordinal))
                throw new ArgumentException("Display chunk does not match the requested level.", nameof(chunks));
            if (chunk.Buckets.Count == 0) continue;

            var first = chunk.Buckets[0].FirstSampleCounter;
            if (previousEnd is { } end && first < end)
                throw new ArgumentException("Display chunks overlap.", nameof(chunks));

            foreach (var bucket in chunk.Buckets)
            {
                if (bucket.IsGap)
                {
                    AppendGap(x, values, ToSeconds(recordingStartSeconds, level, bucket.FirstSampleCounter));
                    continue;
                }

                if (bucket.MinSampleCounter <= bucket.MaxSampleCounter)
                {
                    AppendPoint(x, values, recordingStartSeconds, level, bucket.MinSampleCounter, bucket.MinVolts);
                    if (bucket.MaxSampleCounter != bucket.MinSampleCounter)
                        AppendPoint(x, values, recordingStartSeconds, level,
                            bucket.MaxSampleCounter, bucket.MaxVolts);
                }
                else
                {
                    AppendPoint(x, values, recordingStartSeconds, level, bucket.MaxSampleCounter, bucket.MaxVolts);
                    AppendPoint(x, values, recordingStartSeconds, level,
                        bucket.MinSampleCounter, bucket.MinVolts);
                }
                previousEnd = checked(bucket.FirstSampleCounter + bucket.SampleCount);
            }
        }

        return new ReviewDisplayTracePoints(x, values);
    }

    private static void AppendPoint(
        List<double> x, List<double> values, double recordingStartSeconds,
        ReviewDisplayLevel level, long sampleCounter, double value)
    {
        x.Add(ToSeconds(recordingStartSeconds, level, sampleCounter));
        values.Add(value);
    }

    private static void AppendGap(List<double> x, List<double> values, double seconds)
    {
        if (values.Count > 0 && double.IsNaN(values[^1])) return;
        x.Add(seconds);
        values.Add(double.NaN);
    }

    private static double ToSeconds(double recordingStartSeconds, ReviewDisplayLevel level, long sampleCounter) =>
        recordingStartSeconds + sampleCounter / (double)level.SamplingRateHz;
}

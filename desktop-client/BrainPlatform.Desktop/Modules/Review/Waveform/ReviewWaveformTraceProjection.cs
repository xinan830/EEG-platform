namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ReviewWaveformTracePoints(IReadOnlyList<double> X, IReadOnlyList<double> Y);

/// <summary>Display-only temporal extrema in V-to-screen units; X always remains recording time.</summary>
internal static class ReviewWaveformTraceProjection
{
    internal static ReviewWaveformTracePoints Build(
        RecordingReviewFrame frame, int traceIndex, double baseline, double displayScale, int maximumBuckets)
    {
        var x = new List<double>();
        var y = new List<double>();
        long? previousEndCounter = null;
        foreach (var segment in frame.Segments)
        {
            if (traceIndex >= segment.Channels.Count) continue;
            if (previousEndCounter is { } end && segment.FirstSampleCounter > end)
                AppendGap(x, y, Seconds(frame, end));

            AppendSegment(x, y, segment, segment.Channels[traceIndex].Values,
                frame, baseline, displayScale, maximumBuckets);
            previousEndCounter = segment.FirstSampleCounter + segment.SampleCount;
        }
        return new ReviewWaveformTracePoints(x, y);
    }

    private static void AppendSegment(
        List<double> x, List<double> y, ProjectedMontageSegment segment,
        IReadOnlyList<double> values, RecordingReviewFrame frame,
        double baseline, double displayScale, int maximumBuckets)
    {
        var bucketSize = Math.Max(1, (int)Math.Ceiling(values.Count / (double)maximumBuckets));
        for (var start = 0; start < values.Count; start += bucketSize)
        {
            var end = Math.Min(values.Count, start + bucketSize);
            var first = -1;
            var last = -1;
            var min = -1;
            var max = -1;
            for (var index = start; index < end; index++)
            {
                if (!double.IsFinite(values[index]))
                {
                    // Flush each finite run so decimation cannot bridge an unavailable interval.
                    AppendExtrema(x, y, first, min, max, last, segment, values, frame, baseline, displayScale);
                    first = last = min = max = -1;
                    AppendGap(x, y, Seconds(frame, segment.FirstSampleCounter + index));
                    continue;
                }

                first = first < 0 ? index : first;
                last = index;
                if (min < 0 || values[index] < values[min]) min = index;
                if (max < 0 || values[index] > values[max]) max = index;
            }
            AppendExtrema(x, y, first, min, max, last, segment, values, frame, baseline, displayScale);
        }
    }

    private static void AppendExtrema(
        List<double> x, List<double> y, int first, int min, int max, int last,
        ProjectedMontageSegment segment, IReadOnlyList<double> values,
        RecordingReviewFrame frame, double baseline, double displayScale)
    {
        foreach (var index in new[] { first, min, max, last }.Distinct().Where(index => index >= 0).OrderBy(index => index))
        {
            x.Add(Seconds(frame, segment.FirstSampleCounter + index));
            y.Add(baseline + values[index] * 1_000_000d * displayScale);
        }
    }

    private static void AppendGap(List<double> x, List<double> y, double seconds)
    {
        if (y.Count > 0 && double.IsNaN(y[^1])) return;
        x.Add(seconds);
        y.Add(double.NaN);
    }

    private static double Seconds(RecordingReviewFrame frame, long counter) =>
        frame.WindowStartSeconds + (counter - frame.WindowStartSampleCounter) / (double)frame.SamplingRateHz;
}

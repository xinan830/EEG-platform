using BrainPlatform.Desktop.Modules.Review.Playback;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ReviewSampleMarkerPoints(IReadOnlyList<double> XSeconds, IReadOnlyList<double> ValuesVolts);

/// <summary>Projects real samples from the bounded review frame into marker coordinates.</summary>
internal static class ReviewSampleMarkerProjection
{
    internal const double MinimumSpacingDip = 5;

    internal static bool ShouldShowMarkers(int samplingRateHz, double visibleDurationSeconds, double plotWidthDip) =>
        samplingRateHz > 0 && double.IsFinite(visibleDurationSeconds) && visibleDurationSeconds > 0 &&
        double.IsFinite(plotWidthDip) && plotWidthDip > 0 &&
        plotWidthDip / (samplingRateHz * visibleDurationSeconds) >= MinimumSpacingDip;

    internal static ReviewSampleMarkerPoints Build(RecordingReviewFrame frame, int traceIndex)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (traceIndex < 0 || traceIndex >= frame.OutputChannelNames.Count)
            throw new ArgumentOutOfRangeException(nameof(traceIndex));

        var x = new List<double>();
        var values = new List<double>();
        foreach (var segment in frame.Segments)
        {
            if (traceIndex >= segment.Channels.Count) continue;
            var samples = segment.Channels[traceIndex].Values;
            var count = Math.Min(segment.SampleCount, samples.Length);
            for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
            {
                var value = samples[sampleIndex];
                if (!double.IsFinite(value)) continue;
                var sampleCounter = checked(segment.FirstSampleCounter + sampleIndex);
                x.Add(frame.WindowStartSeconds +
                    (sampleCounter - frame.WindowStartSampleCounter) / (double)frame.SamplingRateHz);
                values.Add(value);
            }
        }

        return new ReviewSampleMarkerPoints(x, values);
    }
}

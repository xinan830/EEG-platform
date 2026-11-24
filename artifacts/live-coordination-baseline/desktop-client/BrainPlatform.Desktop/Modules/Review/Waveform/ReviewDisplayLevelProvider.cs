using BrainPlatform.Desktop.Modules.Review.Cache;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>
/// A chart-only frame. Its traces are already reduced extrema in recording
/// time; it is never used as an input to filtering, montage projection, or an
/// algorithm run.
/// </summary>
internal sealed record ReviewDisplayFrame(
    Guid RecordingSessionId,
    long Revision,
    double WindowStartSeconds,
    double WindowEndSeconds,
    IReadOnlyList<string> OutputChannelNames,
    IReadOnlyList<ReviewDisplayTracePoints> Traces,
    IReadOnlyList<string>? OutputChannelColors,
    string SourceRevision);

/// <summary>
/// Reads a complete persistent display level when one exists. A miss is
/// deliberate and lets the review session keep its bounded source-window path.
/// </summary>
internal sealed class ReviewDisplayLevelProvider
{
    private readonly ReviewDisplayLevelReader reader;

    public ReviewDisplayLevelProvider(ReviewDisplayLevelReader? reader = null) =>
        this.reader = reader ?? new ReviewDisplayLevelReader();

    public async Task<ReviewDisplayFrame?> TryReadAsync(
        RecordingReviewFrame frame,
        double viewportStartSeconds,
        double visibleDurationSeconds,
        int plotWidthPixels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(frame.DisplaySourceIdentity) ||
            !double.IsFinite(viewportStartSeconds) || viewportStartSeconds < 0 ||
            !double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0 ||
            plotWidthPixels <= 0 || frame.OutputChannelNames.Count == 0)
            return null;

        var level = ReviewDisplayLevelSelector.Select(
            visibleDurationSeconds,
            frame.SamplingRateHz,
            plotWidthPixels,
            frame.OutputChannelNames.Count,
            frame.DisplaySourceIdentity);
        var recordingStartSeconds = frame.WindowStartSeconds -
            frame.WindowStartSampleCounter / (double)frame.SamplingRateHz;
        var firstSampleCounter = checked((long)Math.Round(
            (viewportStartSeconds - recordingStartSeconds) * frame.SamplingRateHz,
            MidpointRounding.AwayFromZero));
        if (firstSampleCounter < 0) firstSampleCounter = 0;
        var sampleCount = checked((int)Math.Ceiling(visibleDurationSeconds * frame.SamplingRateHz));
        if (sampleCount <= 0) return null;

        var traces = new ReviewDisplayTracePoints[frame.OutputChannelNames.Count];
        for (var channelIndex = 0; channelIndex < traces.Length; channelIndex++)
        {
            var result = await reader.TryReadAsync(
                level, channelIndex, firstSampleCounter, sampleCount, cancellationToken);
            if (result is null) return null;
            traces[channelIndex] = ReviewDisplayLevelTraceProjection.Build(
                level, result.Chunks, recordingStartSeconds);
        }

        return new ReviewDisplayFrame(
            frame.RecordingSessionId,
            frame.Revision,
            viewportStartSeconds,
            viewportStartSeconds + visibleDurationSeconds,
            frame.OutputChannelNames,
            traces,
            frame.OutputChannelColors,
            frame.SourceRevision);
    }
}

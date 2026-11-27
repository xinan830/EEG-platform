namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>
/// Sole owner of live display timeline continuity: session first-sample anchor,
/// pause-excluded duration accounting, and raw-to-display sample-counter mapping.
/// </summary>
internal sealed class LiveDisplayTimelineTracker
{
    private long? recordingPauseAnchorRawSampleCounter;
    private long? lastRecordedRawSampleCounter;
    private long? lastPausedRawSampleCounter;
    private long pausedRecordingSampleCount;
    private bool awaitingFirstBatchAfterRecordingPause;
    private AcquisitionState lastObservedState = AcquisitionState.NotConfigured;

    public long? SessionFirstSampleCounter { get; private set; }

    public DateTimeOffset? SessionStartUtc { get; private set; }

    public List<LiveDisplayCounterAdjustment> DisplayCounterAdjustments { get; } = [];

    public void OnMetadataChanged(bool wasOrIsNull, DateTimeOffset? recordingStartUtc)
    {
        if (wasOrIsNull)
        {
            SessionStartUtc = recordingStartUtc;
        }

        Reset();
    }

    public void Reset()
    {
        SessionFirstSampleCounter = null;
        recordingPauseAnchorRawSampleCounter = null;
        lastRecordedRawSampleCounter = null;
        lastPausedRawSampleCounter = null;
        pausedRecordingSampleCount = 0;
        awaitingFirstBatchAfterRecordingPause = false;
        lastObservedState = AcquisitionState.NotConfigured;
        DisplayCounterAdjustments.Clear();
    }

    public void Update(AcquisitionState state, IReadOnlyList<AcquisitionBatch> batches, long? recordingStartRawSampleCounter)
    {
        lastObservedState = state;
        if (batches.Count > 0 && SessionFirstSampleCounter is null)
        {
            SessionFirstSampleCounter = batches[0].FirstSampleCounter;
        }

        // Preview batches establish display time, never evidence of recorded samples.
        if (recordingStartRawSampleCounter is null)
        {
            return;
        }

        // The waveform timeline is always raw sample-counter time. Recording
        // pause only excludes a range from raw persistence and its duration;
        // it must never freeze or rewrite the live paper trace.
        if (state == AcquisitionState.Paused)
        {
            if (!awaitingFirstBatchAfterRecordingPause)
            {
                recordingPauseAnchorRawSampleCounter = lastRecordedRawSampleCounter
                    ?? (batches.Count > 0 ? batches[^1].LastSampleCounter : null);
                awaitingFirstBatchAfterRecordingPause = true;
            }

            if (batches.Count > 0)
            {
                lastPausedRawSampleCounter = batches[^1].LastSampleCounter;
            }

            return;
        }

        if (state != AcquisitionState.Recording)
        {
            return;
        }

        if (awaitingFirstBatchAfterRecordingPause)
        {
            if (recordingPauseAnchorRawSampleCounter is { } pauseAnchor)
            {
                var resumeThreshold = lastPausedRawSampleCounter ?? pauseAnchor;
                var firstBatchAfterResume = batches
                    .Where(batch => batch.FirstSampleCounter > resumeThreshold)
                    .OrderBy(batch => batch.FirstSampleCounter)
                    .FirstOrDefault();
                if (firstBatchAfterResume is null)
                {
                    return;
                }

                pausedRecordingSampleCount = checked(pausedRecordingSampleCount +
                    Math.Max(0L, firstBatchAfterResume.FirstSampleCounter - pauseAnchor - 1L));
            }

            recordingPauseAnchorRawSampleCounter = null;
            lastPausedRawSampleCounter = null;
            awaitingFirstBatchAfterRecordingPause = false;
        }

        if (batches.Count > 0)
        {
            lastRecordedRawSampleCounter = batches[^1].LastSampleCounter;
        }
    }

    public DateTimeOffset? GetDisplayClockAnchorUtc(
        AcquisitionStreamMetadata? metadata,
        AcquisitionState state,
        DateTimeOffset? recordingStartUtc,
        long? recordingFirstSampleCounter)
    {
        if (metadata is null || SessionFirstSampleCounter is not { } sessionFirst)
        {
            return null;
        }

        if (state is AcquisitionState.Recording or AcquisitionState.Paused)
        {
            if (recordingStartUtc is not { } startUtc)
            {
                return null;
            }

            if (recordingFirstSampleCounter is not { } recordingFirst)
            {
                return SessionStartUtc;
            }

            var elapsedSamples = ToDisplayCounter(recordingFirst) - ToDisplayCounter(sessionFirst);
            return startUtc.AddSeconds(-elapsedSamples / (double)metadata.SamplingRateHz);
        }

        return SessionStartUtc;
    }

    public TimeSpan GetEffectiveAcquisitionDuration(
        AcquisitionStreamMetadata metadata,
        IReadOnlyList<AcquisitionBatch> batches,
        long? recordingStartRawSampleCounter,
        IReadOnlyList<RecordingLifecycleBoundary>? lifecycleBoundaries = null)
    {
        if (recordingStartRawSampleCounter is not { } startCounter)
        {
            return TimeSpan.Zero;
        }

        if (lifecycleBoundaries is { Count: > 0 })
        {
            return ComputeDurationFromLifecycleBoundaries(metadata, batches, lifecycleBoundaries);
        }

        if (batches.Count == 0)
        {
            return TimeSpan.Zero;
        }

        long effectiveEnd;
        if (awaitingFirstBatchAfterRecordingPause)
        {
            if (recordingPauseAnchorRawSampleCounter is not { } pauseAnchor)
            {
                return TimeSpan.Zero;
            }

            effectiveEnd = pauseAnchor;
        }
        else
        {
            effectiveEnd = batches[^1].LastSampleCounter;
        }

        var sampleCount = Math.Max(0L, effectiveEnd - startCounter + 1L - pausedRecordingSampleCount);
        return TimeSpan.FromSeconds(sampleCount / (double)metadata.SamplingRateHz);
    }

    public long ToDisplayCounter(long rawSampleCounter)
    {
        var skipped = 0L;
        foreach (var adjustment in DisplayCounterAdjustments)
        {
            if (rawSampleCounter < adjustment.EffectiveFromRawSampleCounter)
            {
                break;
            }

            skipped = adjustment.SkippedSampleCountBefore;
        }

        return checked(rawSampleCounter - skipped);
    }

    public long? ToDisplayCounterFromRecordingSample(long recordingRelativeSample, long? recordingFirstSampleCounter)
    {
        if (recordingRelativeSample < 0 || recordingFirstSampleCounter is not { } recordingFirst)
        {
            return null;
        }

        try
        {
            return ToDisplayCounter(checked(recordingFirst + recordingRelativeSample));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private TimeSpan ComputeDurationFromLifecycleBoundaries(
        AcquisitionStreamMetadata metadata,
        IReadOnlyList<AcquisitionBatch> batches,
        IReadOnlyList<RecordingLifecycleBoundary> lifecycleBoundaries)
    {
        long? openSegmentStart = null;
        var totalSamples = 0L;
        foreach (var boundary in lifecycleBoundaries)
        {
            switch (boundary.Kind)
            {
                case RecordingLifecycleBoundaryKind.Started:
                case RecordingLifecycleBoundaryKind.Resumed:
                    openSegmentStart ??= boundary.SampleCounter;
                    break;
                case RecordingLifecycleBoundaryKind.Paused:
                case RecordingLifecycleBoundaryKind.Stopped:
                    if (openSegmentStart is { } start && boundary.SampleCounter >= start)
                    {
                        totalSamples = checked(totalSamples + (boundary.SampleCounter - start + 1L));
                        openSegmentStart = null;
                    }
                    break;
            }
        }

        if (openSegmentStart is { } activeStart &&
            lastObservedState == AcquisitionState.Recording &&
            batches.Count > 0 &&
            batches[^1].LastSampleCounter >= activeStart)
        {
            totalSamples = checked(totalSamples + (batches[^1].LastSampleCounter - activeStart + 1L));
        }

        return TimeSpan.FromSeconds(totalSamples / (double)metadata.SamplingRateHz);
    }
}

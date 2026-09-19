using BrainPlatform.Desktop.ViewModels;

using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.Views;

/// <summary>
/// Converts a retained raw acquisition snapshot into screen-density extrema.
/// Values remain in V here. Unit conversion occurs only in the renderer.
/// </summary>
public static class WaveformDisplayFrameBuilder
{
    public static WaveformDisplayFrame? Build(
        LiveWaveformSource? source,
        double displayWindowSeconds,
        int horizontalPixels)
    {
        if (source is null || source.Batches.Count == 0 || source.Channels.Count == 0)
        {
            return null;
        }

        if (!double.IsFinite(displayWindowSeconds) || displayWindowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayWindowSeconds));
        }

        var adjustments = source.DisplayCounterAdjustments ?? [];
        var sessionFirstCounter = ToDisplayCounter(
            source.SessionFirstSampleCounter ?? source.Batches[0].FirstSampleCounter,
            adjustments);
        var lastBatch = source.Batches[^1];
        var lastCounter = checked(ToDisplayCounter(lastBatch.FirstSampleCounter, adjustments) + lastBatch.SampleCount - 1L);
        if (lastCounter < sessionFirstCounter)
        {
            return null;
        }

        var pageSamples = checked((int)Math.Max(
            1d,
            Math.Round(
                source.Metadata.SamplingRateHz * displayWindowSeconds,
                MidpointRounding.AwayFromZero)));
        var elapsedSamples = checked(lastCounter - sessionFirstCounter + 1L);
        var pageIndex = (elapsedSamples - 1L) / pageSamples;
        var pageStart = checked(sessionFirstCounter + pageIndex * pageSamples);
        var cursorSamples = checked((int)(lastCounter - pageStart + 1L));
        var bucketSize = Math.Max(1, pageSamples / Math.Max(1, horizontalPixels));
        var spans = BuildDisplaySpans(pageStart, cursorSamples, pageSamples, sessionFirstCounter);
        var displayBatches = SelectDisplayBatches(source.Batches, adjustments, spans);
        var filterBoundaries = (source.DisplayFilterBoundaries ?? []).ToHashSet();

        var traces = BuildTraces(source, displayBatches, spans, bucketSize, filterBoundaries);
        var cursorSeconds = cursorSamples / (double)source.Metadata.SamplingRateHz;
        return new WaveformDisplayFrame(
            pageStart,
            pageSamples,
            displayWindowSeconds,
            source.Metadata.SamplingRateHz,
            cursorSeconds,
            (pageStart - sessionFirstCounter) / (double)source.Metadata.SamplingRateHz,
            traces);
    }

    private static IReadOnlyList<WaveformDisplayTrace> BuildTraces(
        LiveWaveformSource source,
        IReadOnlyList<DisplayBatch> displayBatches,
        IReadOnlyList<DisplaySpan> spans,
        int bucketSize,
        IReadOnlySet<long> filterBoundaries)
    {
        var montageSourceChannels = source.MontageSourceChannels ?? source.Channels;
        var channelsByLabel = montageSourceChannels
            .Where(channel => channel.StreamIndex >= 0 && !string.IsNullOrWhiteSpace(channel.Label))
            .GroupBy(channel => channel.Label, StringComparer.OrdinalIgnoreCase)
            // A montage label must resolve to exactly one raw source. Duplicate
            // labels are ambiguous, so omit them and let the derived trace be
            // skipped rather than displaying an arbitrary channel.
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().StreamIndex, StringComparer.OrdinalIgnoreCase);
        if (source.MontageProfile is null)
        {
            return source.Channels
                .Where(channel => channel.IsVisible)
                .Select(channel => new WaveformDisplayTrace(
                    channel.Label,
                    BuildTrace(displayBatches, channel.StreamIndex, spans, bucketSize, filterBoundaries,
                        (batch, sampleIndex) => ReadValue(batch, sampleIndex, channel.StreamIndex))))
                .ToArray();
        }

        var traces = new List<WaveformDisplayTrace>();
        foreach (var montageChannel in source.MontageProfile.DerivedChannels.OrderBy(channel => channel.DisplayOrder))
        {
            if (!channelsByLabel.TryGetValue(montageChannel.PositiveLabel, out var positiveIndex))
            {
                continue;
            }

            var negativeIndexes = montageChannel.NegativeLabels
                .Select(label => channelsByLabel.GetValueOrDefault(label, -1))
                .ToArray();
            if (montageChannel.NegativeKind != MontageNegativeKind.OriginalHardwareReference &&
                negativeIndexes.Any(index => index < 0))
            {
                continue;
            }

            traces.Add(new WaveformDisplayTrace(
                montageChannel.Name,
                BuildTrace(
                    displayBatches,
                    positiveIndex,
                    spans,
                    bucketSize,
                    filterBoundaries,
                    (batch, sampleIndex) => CalculateMontageValue(batch, sampleIndex, positiveIndex, montageChannel.NegativeKind, negativeIndexes))));
        }

        return traces;
    }

    private static double CalculateMontageValue(
        AcquisitionBatch batch,
        int sampleIndex,
        int positiveIndex,
        MontageNegativeKind negativeKind,
        IReadOnlyList<int> negativeIndexes)
    {
        var positive = ReadValue(batch, sampleIndex, positiveIndex);
        if (negativeKind == MontageNegativeKind.OriginalHardwareReference)
        {
            return positive;
        }

        if (!double.IsFinite(positive) || negativeIndexes.Count == 0)
        {
            return double.NaN;
        }

        // This method runs for every raw sample in every visible derived trace.
        // Avoid LINQ and per-sample arrays here: at 4 kHz those allocations can
        // otherwise dominate the UI process and starve WPF rendering.
        var referenceSum = 0d;
        for (var index = 0; index < negativeIndexes.Count; index++)
        {
            var referenceValue = ReadValue(batch, sampleIndex, negativeIndexes[index]);
            if (!double.IsFinite(referenceValue))
            {
                return double.NaN;
            }

            referenceSum += referenceValue;
        }

        return positive - referenceSum / negativeIndexes.Count;
    }

    private static double ReadValue(AcquisitionBatch batch, int sampleIndex, int streamIndex) =>
        streamIndex >= 0 && streamIndex < batch.ChannelCount
            ? batch.SampleMajorValues[checked(sampleIndex * batch.ChannelCount + streamIndex)]
            : double.NaN;

    private static IReadOnlyList<DisplayBatch> SelectDisplayBatches(
        IReadOnlyList<BrainPlatform.Desktop.Acquisition.Contracts.AcquisitionBatch> batches,
        IReadOnlyList<LiveDisplayCounterAdjustment> adjustments,
        IReadOnlyList<DisplaySpan> spans)
    {
        var selected = new List<DisplayBatch>();
        var firstRequiredCounter = spans.Min(span => span.FirstSampleCounter);
        var lastRequiredCounter = spans.Max(span => span.LastSampleCounter);
        for (var index = batches.Count - 1; index >= 0; index--)
        {
            var batch = batches[index];
            var displayBatch = new DisplayBatch(batch, ToDisplayCounter(batch.FirstSampleCounter, adjustments));
            if (displayBatch.LastDisplaySampleCounter < firstRequiredCounter)
            {
                break;
            }
            if (displayBatch.FirstDisplaySampleCounter > lastRequiredCounter)
            {
                continue;
            }
            if (spans.Any(span =>
                    displayBatch.FirstDisplaySampleCounter <= span.LastSampleCounter &&
                    displayBatch.LastDisplaySampleCounter >= span.FirstSampleCounter))
            {
                selected.Add(displayBatch);
            }
        }

        selected.Reverse();
        return selected;
    }

    private static long ToDisplayCounter(
        long rawSampleCounter,
        IReadOnlyList<LiveDisplayCounterAdjustment> adjustments)
    {
        var skipped = 0L;
        foreach (var adjustment in adjustments)
        {
            if (rawSampleCounter < adjustment.EffectiveFromRawSampleCounter)
            {
                break;
            }

            skipped = adjustment.SkippedSampleCountBefore;
        }

        return checked(rawSampleCounter - skipped);
    }

    private static IReadOnlyList<DisplaySpan> BuildDisplaySpans(
        long pageStart,
        int cursorSamples,
        int pageSamples,
        long sessionFirstCounter)
    {
        var spans = new List<DisplaySpan>(2)
        {
            new(pageStart, checked(pageStart + cursorSamples - 1L), 0),
        };
        if (pageStart > sessionFirstCounter && cursorSamples < pageSamples)
        {
            var previousPageStart = checked(pageStart - pageSamples);
            spans.Add(new DisplaySpan(
                checked(previousPageStart + cursorSamples),
                checked(pageStart - 1L),
                cursorSamples));
        }

        return spans;
    }

    private static IReadOnlyList<WaveformDisplayPoint> BuildTrace(
        IReadOnlyList<DisplayBatch> displayBatches,
        int streamIndex,
        IReadOnlyList<DisplaySpan> spans,
        int bucketSize,
        IReadOnlySet<long> filterBoundaries,
        Func<AcquisitionBatch, int, double> valueSelector)
    {
        if (streamIndex < 0)
        {
            return [];
        }

        var points = new List<WaveformDisplayPoint>();
        foreach (var span in spans)
        {
            long? expectedCounter = null;
            var startsSegment = true;
            var bucket = default(BucketAccumulator);
            var hasBucket = false;
            foreach (var displayBatch in displayBatches)
            {
                var batch = displayBatch.Batch;
                if (streamIndex >= batch.ChannelCount)
                {
                    FlushBucket(points, ref bucket, ref hasBucket);
                    expectedCounter = null;
                    startsSegment = true;
                    continue;
                }

                var first = Math.Max(displayBatch.FirstDisplaySampleCounter, span.FirstSampleCounter);
                var last = Math.Min(displayBatch.LastDisplaySampleCounter, span.LastSampleCounter);
                if (first > last)
                {
                    continue;
                }

                var firstIndex = checked((int)(first - displayBatch.FirstDisplaySampleCounter));
                var lastIndex = checked((int)(last - displayBatch.FirstDisplaySampleCounter));
                var firstRawCounter = checked(batch.FirstSampleCounter + firstIndex);
                if (expectedCounter is not null && firstRawCounter != expectedCounter.Value)
                {
                    FlushBucket(points, ref bucket, ref hasBucket);
                    startsSegment = true;
                }

                for (var sampleIndex = firstIndex; sampleIndex <= lastIndex; sampleIndex++)
                {
                    var sampleCounter = checked(batch.FirstSampleCounter + sampleIndex);
                    if (filterBoundaries.Contains(sampleCounter))
                    {
                        FlushBucket(points, ref bucket, ref hasBucket);
                        startsSegment = true;
                    }

                    var value = valueSelector(batch, sampleIndex);
                    if (!double.IsFinite(value))
                    {
                        FlushBucket(points, ref bucket, ref hasBucket);
                        startsSegment = true;
                        continue;
                    }

                    var displaySampleCounter = checked(displayBatch.FirstDisplaySampleCounter + sampleIndex);
                    var displayOffset = checked(span.DisplayStartSampleOffset + displaySampleCounter - span.FirstSampleCounter);
                    var bucketIndex = displayOffset / bucketSize;
                    if (!hasBucket || bucket.BucketIndex != bucketIndex)
                    {
                        FlushBucket(points, ref bucket, ref hasBucket);
                        bucket = new BucketAccumulator(
                            bucketIndex,
                            sampleCounter,
                            displayOffset,
                            value,
                            sampleCounter,
                            displayOffset,
                            value,
                            sampleCounter,
                            displayOffset,
                            value,
                            sampleCounter,
                            displayOffset,
                            value,
                            startsSegment);
                        hasBucket = true;
                        startsSegment = false;
                    }
                    else
                    {
                        bucket.LastSampleCounter = sampleCounter;
                        bucket.LastDisplaySampleOffset = displayOffset;
                        bucket.LastVolts = value;
                        if (value < bucket.MinVolts)
                        {
                            bucket.MinSampleCounter = sampleCounter;
                            bucket.MinDisplaySampleOffset = displayOffset;
                            bucket.MinVolts = value;
                        }

                        if (value > bucket.MaxVolts)
                        {
                            bucket.MaxSampleCounter = sampleCounter;
                            bucket.MaxDisplaySampleOffset = displayOffset;
                            bucket.MaxVolts = value;
                        }
                    }
                }

                expectedCounter = checked(batch.FirstSampleCounter + lastIndex + 1L);
            }

            FlushBucket(points, ref bucket, ref hasBucket);
        }

        return points;
    }

    private static void FlushBucket(
        List<WaveformDisplayPoint> points,
        ref BucketAccumulator bucket,
        ref bool hasBucket)
    {
        if (!hasBucket)
        {
            return;
        }

        long? lastEmittedCounter = null;
        AppendTemporalSample(
            points,
            ref lastEmittedCounter,
            bucket.SampleCounter,
            bucket.DisplaySampleOffset,
            bucket.FirstVolts,
            bucket.StartsSegment);
        if (bucket.MinSampleCounter <= bucket.MaxSampleCounter)
        {
            AppendTemporalSample(points, ref lastEmittedCounter, bucket.MinSampleCounter, bucket.MinDisplaySampleOffset, bucket.MinVolts, false);
            AppendTemporalSample(points, ref lastEmittedCounter, bucket.MaxSampleCounter, bucket.MaxDisplaySampleOffset, bucket.MaxVolts, false);
        }
        else
        {
            AppendTemporalSample(points, ref lastEmittedCounter, bucket.MaxSampleCounter, bucket.MaxDisplaySampleOffset, bucket.MaxVolts, false);
            AppendTemporalSample(points, ref lastEmittedCounter, bucket.MinSampleCounter, bucket.MinDisplaySampleOffset, bucket.MinVolts, false);
        }

        AppendTemporalSample(
            points,
            ref lastEmittedCounter,
            bucket.LastSampleCounter,
            bucket.LastDisplaySampleOffset,
            bucket.LastVolts,
            false);
        bucket = default;
        hasBucket = false;
    }

    private static void AppendTemporalSample(
        List<WaveformDisplayPoint> points,
        ref long? lastEmittedCounter,
        long sampleCounter,
        long displaySampleOffset,
        double volts,
        bool startsSegment)
    {
        if (lastEmittedCounter == sampleCounter)
        {
            return;
        }

        points.Add(new WaveformDisplayPoint(
            sampleCounter,
            displaySampleOffset,
            volts,
            volts,
            startsSegment));
        lastEmittedCounter = sampleCounter;
    }

    private sealed record DisplaySpan(
        long FirstSampleCounter,
        long LastSampleCounter,
        long DisplayStartSampleOffset);

    private sealed record DisplayBatch(
        BrainPlatform.Desktop.Acquisition.Contracts.AcquisitionBatch Batch,
        long FirstDisplaySampleCounter)
    {
        public long LastDisplaySampleCounter => checked(FirstDisplaySampleCounter + Batch.SampleCount - 1L);
    }

    private struct BucketAccumulator(
        long bucketIndex,
        long sampleCounter,
        long displaySampleOffset,
        double firstVolts,
        long lastSampleCounter,
        long lastDisplaySampleOffset,
        double lastVolts,
        long minSampleCounter,
        long minDisplaySampleOffset,
        double minVolts,
        long maxSampleCounter,
        long maxDisplaySampleOffset,
        double maxVolts,
        bool startsSegment)
    {
        public long BucketIndex = bucketIndex;
        public long SampleCounter = sampleCounter;
        public long DisplaySampleOffset = displaySampleOffset;
        public double FirstVolts = firstVolts;
        public long LastSampleCounter = lastSampleCounter;
        public long LastDisplaySampleOffset = lastDisplaySampleOffset;
        public double LastVolts = lastVolts;
        public long MinSampleCounter = minSampleCounter;
        public long MinDisplaySampleOffset = minDisplaySampleOffset;
        public double MinVolts = minVolts;
        public long MaxSampleCounter = maxSampleCounter;
        public long MaxDisplaySampleOffset = maxDisplaySampleOffset;
        public double MaxVolts = maxVolts;
        public bool StartsSegment = startsSegment;
    }

}

public sealed record WaveformDisplayFrame(
    long WindowStartSampleCounter,
    int WindowSampleCount,
    double DisplayWindowSeconds,
    int SamplingRateHz,
    double CursorSeconds,
    double PageStartElapsedSeconds,
    IReadOnlyList<WaveformDisplayTrace> Traces);

public sealed record WaveformDisplayTrace(string Label, IReadOnlyList<WaveformDisplayPoint> Points);

public sealed record WaveformDisplayPoint(
    long SampleCounter,
    long DisplaySampleOffset,
    double MinVolts,
    double MaxVolts,
    bool StartsSegment);

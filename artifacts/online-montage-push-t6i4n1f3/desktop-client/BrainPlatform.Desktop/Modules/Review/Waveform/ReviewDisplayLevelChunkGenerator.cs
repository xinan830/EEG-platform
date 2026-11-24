using System;
using System.Collections.Generic;
using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Montages;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>
/// Builds one disposable display chunk from already projected V/float64 traces.
/// It never filters, references, or mutates the scientific source arrays.
/// </summary>
internal static class ReviewDisplayLevelChunkGenerator
{
    public static ReviewDisplayLevelChunk Build(
        ReviewDisplayLevel level,
        int channelIndex,
        int chunkIndex,
        long firstSampleCounter,
        int sampleCount,
        IReadOnlyList<ProjectedMontageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(segments);
        if (channelIndex < 0 || channelIndex >= level.ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (chunkIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        if (firstSampleCounter < 0)
            throw new ArgumentOutOfRangeException(nameof(firstSampleCounter));
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        var endExclusive = checked(firstSampleCounter + sampleCount);
        var bucketSize = level.SamplesPerBucket;
        var firstBucket = firstSampleCounter / bucketSize;
        var lastBucket = (endExclusive - 1) / bucketSize;
        var bucketCount = checked(lastBucket - firstBucket + 1);
        if (bucketCount > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Display chunk exceeds the bucket safety limit.");

        var states = new Dictionary<long, BucketState>();
        foreach (var segment in segments)
        {
            ValidateSegment(segment, channelIndex);
            var segmentEnd = checked(segment.FirstSampleCounter + segment.SampleCount);
            var overlapStart = Math.Max(firstSampleCounter, segment.FirstSampleCounter);
            var overlapEnd = Math.Min(endExclusive, segmentEnd);
            if (overlapStart >= overlapEnd) continue;

            var values = segment.Channels[channelIndex].Values;
            for (var counter = overlapStart; counter < overlapEnd; counter++)
            {
                var valueIndex = checked((int)(counter - segment.FirstSampleCounter));
                var bucketIndex = counter / bucketSize;
                states.TryGetValue(bucketIndex, out var state);
                state.CoveredCount++;
                if (!double.IsFinite(values[valueIndex]))
                {
                    state.IsGap = true;
                    state.HasValue = false;
                    states[bucketIndex] = state;
                    continue;
                }

                if (!state.IsGap && !state.HasValue)
                {
                    state.Min = values[valueIndex];
                    state.Max = values[valueIndex];
                    state.MinCounter = counter;
                    state.MaxCounter = counter;
                    state.HasValue = true;
                }
                else if (!state.IsGap)
                {
                    if (values[valueIndex] < state.Min)
                    {
                        state.Min = values[valueIndex];
                        state.MinCounter = counter;
                    }
                    if (values[valueIndex] > state.Max)
                    {
                        state.Max = values[valueIndex];
                        state.MaxCounter = counter;
                    }
                }
                states[bucketIndex] = state;
            }
        }

        var buckets = new List<ReviewDisplayBucket>(checked((int)bucketCount));
        for (var bucketIndex = firstBucket; bucketIndex <= lastBucket; bucketIndex++)
        {
            var bucketStart = Math.Max(firstSampleCounter, checked(bucketIndex * bucketSize));
            var bucketEnd = Math.Min(endExclusive, checked((bucketIndex + 1) * bucketSize));
            var count = checked((int)(bucketEnd - bucketStart));
            var expectedCount = count;
            if (!states.TryGetValue(bucketIndex, out var state) ||
                state.CoveredCount != expectedCount || state.IsGap || !state.HasValue)
            {
                buckets.Add(new ReviewDisplayBucket(bucketStart, count, true, -1, -1, double.NaN, double.NaN));
            }
            else
            {
                buckets.Add(new ReviewDisplayBucket(bucketStart, count, false,
                    state.MinCounter, state.MaxCounter, state.Min, state.Max));
            }
        }

        return new ReviewDisplayLevelChunk(level.Identity, chunkIndex, buckets);
    }

    private static void ValidateSegment(ProjectedMontageSegment segment, int channelIndex)
    {
        if (segment.SampleCount <= 0 || segment.Channels.Count <= channelIndex)
            throw new ArgumentException("Projected display segment is incomplete.", nameof(segment));
        var values = segment.Channels[channelIndex].Values;
        if (values.Length != segment.SampleCount)
            throw new ArgumentException("Projected display segment value count does not match its sample count.", nameof(segment));
    }

    private struct BucketState
    {
        public bool HasValue;
        public bool IsGap;
        public int CoveredCount;
        public long MinCounter;
        public long MaxCounter;
        public double Min;
        public double Max;
    }
}

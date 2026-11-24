using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Waveform;

/// <summary>Joins display-only preview history across an impedance interruption.</summary>
internal sealed class PreviewDisplayContinuity
{
    private readonly LiveWaveformSource frozen;
    private readonly long firstDisplayCounter;
    private readonly long lastFrozenDisplayCounter;
    private readonly int retainedPageSamples;
    private IReadOnlyList<AcquisitionBatch>? lastResumedBatches;
    private LiveWaveformSource? lastComposed;
    private long? resumedCounterOffset;

    private PreviewDisplayContinuity(LiveWaveformSource frozen, int retainedPageSamples)
    {
        this.frozen = frozen;
        this.retainedPageSamples = retainedPageSamples;
        firstDisplayCounter = frozen.SessionFirstSampleCounter ?? frozen.Batches[0].FirstSampleCounter;
        lastFrozenDisplayCounter = frozen.Batches[^1].LastSampleCounter;
    }

    public LiveWaveformSource FrozenSource => frozen;

    public static PreviewDisplayContinuity? Freeze(LiveWaveformSource source, double visibleSeconds)
    {
        if (source.Batches.Count == 0 || source.Metadata.SamplingRateHz <= 0 ||
            !double.IsFinite(visibleSeconds) || visibleSeconds <= 0)
        {
            return null;
        }

        var adjustments = source.DisplayCounterAdjustments ?? [];
        var lastCounter = MapCounter(source.Batches[^1].LastSampleCounter, adjustments);
        var pageSamples = checked((int)Math.Max(1d, Math.Round(
            source.Metadata.SamplingRateHz * visibleSeconds, MidpointRounding.AwayFromZero)));
        var firstVisibleCounter = Math.Max(
            MapCounter(source.SessionFirstSampleCounter ?? source.Batches[0].FirstSampleCounter, adjustments),
            checked(lastCounter - pageSamples + 1L));
        var batches = new List<AcquisitionBatch>();
        foreach (var batch in source.Batches)
        {
            var displayFirst = MapCounter(batch.FirstSampleCounter, adjustments);
            var displayLast = checked(displayFirst + batch.SampleCount - 1L);
            var first = Math.Max(firstVisibleCounter, displayFirst);
            var last = Math.Min(lastCounter, displayLast);
            if (first > last)
            {
                continue;
            }

            var sourceOffset = checked((int)(first - displayFirst));
            var count = checked((int)(last - first + 1L));
            var values = new double[checked(count * batch.ChannelCount)];
            Array.Copy(batch.SampleMajorValues, checked(sourceOffset * batch.ChannelCount), values, 0, values.Length);
            batches.Add(new AcquisitionBatch(first, count, batch.ChannelCount, values, batch.ReceivedAtUtc));
        }

        if (batches.Count == 0)
        {
            return null;
        }

        var mappedBoundaries = (source.DisplayFilterBoundaries ?? [])
            .Select(counter => MapCounter(counter, adjustments))
            .Where(counter => counter >= firstVisibleCounter && counter <= lastCounter)
            .ToArray();
        var sessionFirstCounter = MapCounter(
            source.SessionFirstSampleCounter ?? source.Batches[0].FirstSampleCounter,
            adjustments);
        var snapshot = source with
        {
            Batches = batches,
            SessionFirstSampleCounter = sessionFirstCounter,
            DisplayCounterAdjustments = [],
            DisplayFilterBoundaries = mappedBoundaries,
        };
        return new PreviewDisplayContinuity(snapshot, pageSamples);
    }

    public LiveWaveformSource Compose(LiveWaveformSource resumed)
    {
        if (resumed.Batches.Count == 0)
        {
            return frozen;
        }

        if (!HasCompatibleLayout(frozen, resumed))
        {
            return resumed;
        }

        if (ReferenceEquals(lastResumedBatches, resumed.Batches) && lastComposed is not null)
        {
            var hasFrozenSamples = resumed.Batches[^1].LastSampleCounter + resumedCounterOffset.GetValueOrDefault() -
                retainedPageSamples + 1L <= lastFrozenDisplayCounter;
            var mappedBoundaries = (resumed.DisplayFilterBoundaries ?? [])
                .Select(counter => checked(counter + resumedCounterOffset.GetValueOrDefault()));
            lastComposed = lastComposed with
            {
                Channels = resumed.Channels,
                MontageProfile = resumed.MontageProfile,
                MontageSourceChannels = resumed.MontageSourceChannels,
                VisibleMontageChannelNames = resumed.VisibleMontageChannelNames,
                DisplayFilterBoundaries = hasFrozenSamples
                    ? frozen.DisplayFilterBoundaries!.Concat(mappedBoundaries).Distinct().ToArray()
                    : mappedBoundaries.ToArray(),
            };
            return lastComposed;
        }

        resumedCounterOffset ??= checked(lastFrozenDisplayCounter + 2L - resumed.Batches[0].FirstSampleCounter);
        var resumedBatches = resumed.Batches.Select(batch => new AcquisitionBatch(
            checked(batch.FirstSampleCounter + resumedCounterOffset.Value),
            batch.SampleCount,
            batch.ChannelCount,
            batch.SampleMajorValues,
            batch.ReceivedAtUtc)).ToArray();
        var latest = resumedBatches[^1].LastSampleCounter;
        var keepFrozen = latest - retainedPageSamples + 1L <= lastFrozenDisplayCounter;
        var boundaries = (resumed.DisplayFilterBoundaries ?? [])
            .Select(counter => checked(counter + resumedCounterOffset.Value));
        var composed = resumed with
        {
            Batches = keepFrozen ? frozen.Batches.Concat(resumedBatches).ToArray() : resumedBatches,
            SessionFirstSampleCounter = firstDisplayCounter,
            DisplayCounterAdjustments = [],
            DisplayFilterBoundaries = keepFrozen
                ? frozen.DisplayFilterBoundaries!.Concat(boundaries).Distinct().ToArray()
                : boundaries.ToArray(),
        };
        lastResumedBatches = resumed.Batches;
        lastComposed = composed;
        return composed;
    }

    private static bool HasCompatibleLayout(LiveWaveformSource left, LiveWaveformSource right) =>
        left.Metadata.SamplingRateHz == right.Metadata.SamplingRateHz &&
        left.Metadata.Channels.Count == right.Metadata.Channels.Count &&
        left.Metadata.Channels.Zip(right.Metadata.Channels).All(pair =>
            pair.First.StreamIndex == pair.Second.StreamIndex &&
            pair.First.NativeChannelIndex == pair.Second.NativeChannelIndex &&
            pair.First.Kind == pair.Second.Kind &&
            string.Equals(pair.First.Label, pair.Second.Label, StringComparison.OrdinalIgnoreCase));

    private static long MapCounter(long counter, IReadOnlyList<LiveDisplayCounterAdjustment> adjustments)
    {
        var skipped = 0L;
        foreach (var adjustment in adjustments)
        {
            if (counter < adjustment.EffectiveFromRawSampleCounter)
            {
                break;
            }

            skipped = adjustment.SkippedSampleCountBefore;
        }

        return checked(counter - skipped);
    }
}

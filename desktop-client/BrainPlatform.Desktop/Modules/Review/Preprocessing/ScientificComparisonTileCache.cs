using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Bounded LRU tile buffer for paired scientific comparison envelopes (no frontend filtering/resampling).</summary>
internal sealed class ScientificComparisonTileCache
{
    private const int MaxCachedTiles = 12;
    private readonly record struct ScopeKey(long Revision, string RecordingId, string? MontageId, int PointBudget, double WindowSeconds);

    private ScopeKey? scope;
    private readonly List<PreprocessingPreviewWindowResponse> tiles = [];
    private readonly Dictionary<string, PreprocessingPreviewWindowResponse> mergedChainCache = new(StringComparer.Ordinal);

    public void Clear()
    {
        scope = null;
        tiles.Clear();
        mergedChainCache.Clear();
    }

    public bool MatchesScope(long revision, string recordingId, string? montageId, int pointBudget, double windowSeconds) =>
        scope is { } current &&
        current.Revision == revision &&
        string.Equals(current.RecordingId, recordingId, StringComparison.Ordinal) &&
        string.Equals(current.MontageId, montageId, StringComparison.Ordinal) &&
        current.PointBudget == pointBudget &&
        Math.Abs(current.WindowSeconds - windowSeconds) <= 1e-6;

    public void Put(long revision, double windowSeconds, PreprocessingPreviewWindowResponse tile)
    {
        var nextScope = new ScopeKey(
            revision,
            tile.RecordingId,
            tile.MontageId,
            tile.DisplayCompression.TargetPointsPerChannel,
            windowSeconds);
        if (scope != nextScope)
        {
            tiles.Clear();
            scope = nextScope;
        }
        else if (tiles.Count > 0 && !AreMetadataCompatible(tiles[0], tile))
        {
            tiles.Clear();
        }

        var existingIndex = tiles.FindIndex(item => item.StartSample == tile.StartSample && item.EndSample == tile.EndSample);
        if (existingIndex >= 0) tiles.RemoveAt(existingIndex);
        tiles.Add(tile);
        while (tiles.Count > MaxCachedTiles)
            tiles.RemoveAt(0);
        mergedChainCache.Clear();
    }

    public IReadOnlyList<(double StartSeconds, double WindowSeconds)> PlanAlignedTiles(
        long revision, int pointBudget, double viewportStart, double viewportWindow, double recordingDuration, int neighborMarginTiles = 0)
    {
        var window = Math.Clamp(viewportWindow, 0.05, 60.0);
        var scopeValid = scope is { } current &&
            current.Revision == revision &&
            current.PointBudget == pointBudget &&
            Math.Abs(current.WindowSeconds - window) <= 1e-6;
        var duration = double.IsFinite(recordingDuration) && recordingDuration > 0
            ? recordingDuration
            : viewportStart + window;
        var clampedStart = Math.Clamp(viewportStart, 0, Math.Max(0, duration - 1e-6));
        var clampedEnd = Math.Min(duration, clampedStart + window);
        var firstIdx = Math.Max(0, (int)Math.Floor(clampedStart / window) - neighborMarginTiles);
        var lastIdx = (int)Math.Floor(Math.Max(clampedStart, clampedEnd - 1e-6) / window) + neighborMarginTiles;
        var maxIdx = Math.Max(0, (int)Math.Floor(Math.Max(0, duration - 1e-6) / window));
        lastIdx = Math.Min(lastIdx, maxIdx);

        var planned = new List<(double StartSeconds, double WindowSeconds)>();
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            var tileStart = idx * window;
            if (tileStart >= duration - 1e-6 && idx > 0) continue;
            var tileWindow = Math.Min(window, Math.Max(0.05, duration - tileStart));
            if (tileWindow <= 1e-6) continue;
            if (!scopeValid || !IsRangeCoveredBySingleTile(tileStart, tileStart + tileWindow))
                planned.Add((tileStart, tileWindow));
        }
        return planned;
    }

    public bool TryComposeForViewport(
        long revision,
        double viewportStart,
        double viewportWindow,
        double recordingDuration,
        out PreprocessingPreviewWindowResponse? composed,
        out bool fullyCovered)
    {
        composed = null;
        fullyCovered = false;
        if (scope is not { } current || current.Revision != revision ||
            Math.Abs(current.WindowSeconds - viewportWindow) > 1e-6 || tiles.Count == 0)
            return false;

        var duration = double.IsFinite(recordingDuration) && recordingDuration > 0
            ? recordingDuration
            : viewportStart + viewportWindow;
        var targetStart = Math.Max(0, viewportStart);
        var targetEnd = Math.Min(duration, targetStart + viewportWindow);
        var sampleTol = 1.0 / tiles[0].SamplingRateHz + 1e-4;

        var relevant = tiles
            .Where(t => t.EndSeconds >= targetStart - sampleTol && t.StartSeconds <= targetEnd + sampleTol)
            .OrderBy(t => t.StartSample)
            .ThenByDescending(t => t.EndSample)
            .ToList();
        if (relevant.Count == 0) return false;

        // Deduplicate nested tiles and include immediate contiguous cached neighbors so SciChart can pan smoothly.
        var ordered = tiles.OrderBy(t => t.StartSample).ThenByDescending(t => t.EndSample).ToList();
        var chain = BuildContiguousChain(ordered, relevant[0]);
        if (chain.Count == 1)
        {
            composed = chain[0];
        }
        else
        {
            var chainKey = string.Join("|", chain.Select(t => $"{t.StartSample}:{t.EndSample}"));
            if (!mergedChainCache.TryGetValue(chainKey, out composed))
            {
                composed = MergeContiguousTiles(chain);
                mergedChainCache[chainKey] = composed;
            }
        }
        fullyCovered = composed.StartSeconds <= targetStart + sampleTol &&
                       composed.EndSeconds >= targetEnd - sampleTol;
        return true;
    }

    private bool IsRangeCoveredBySingleTile(double start, double end)
    {
        if (tiles.Count == 0) return false;
        var tol = 1.0 / tiles[0].SamplingRateHz + 1e-4;
        return tiles.Any(t => t.StartSeconds <= start + tol && t.EndSeconds >= end - tol);
    }

    private static List<PreprocessingPreviewWindowResponse> BuildContiguousChain(
        List<PreprocessingPreviewWindowResponse> ordered, PreprocessingPreviewWindowResponse seed)
    {
        var chain = new List<PreprocessingPreviewWindowResponse> { seed };
        var seedIndex = ordered.IndexOf(seed);
        var leftStart = seed.StartSample;
        for (var i = seedIndex - 1; i >= 0; i--)
        {
            var candidate = ordered[i];
            if (candidate.EndSample + 1 < leftStart) break;
            if (candidate.StartSample >= leftStart) continue;
            chain.Insert(0, candidate);
            leftStart = candidate.StartSample;
        }
        var rightEnd = seed.EndSample;
        for (var i = seedIndex + 1; i < ordered.Count; i++)
        {
            var candidate = ordered[i];
            if (candidate.StartSample > rightEnd + 1) break;
            if (candidate.EndSample <= rightEnd) continue;
            chain.Add(candidate);
            rightEnd = candidate.EndSample;
        }
        return chain;
    }

    private static bool AreMetadataCompatible(PreprocessingPreviewWindowResponse a, PreprocessingPreviewWindowResponse b) =>
        string.Equals(a.RecordingId, b.RecordingId, StringComparison.Ordinal) &&
        string.Equals(a.SourceSha256, b.SourceSha256, StringComparison.Ordinal) &&
        string.Equals(a.MontageId, b.MontageId, StringComparison.Ordinal) &&
        string.Equals(a.MontageSnapshotSha256, b.MontageSnapshotSha256, StringComparison.Ordinal) &&
        a.EnhancedPreprocessingActive == b.EnhancedPreprocessingActive &&
        string.Equals(a.CommonFilter.SpecFingerprint, b.CommonFilter.SpecFingerprint, StringComparison.Ordinal) &&
        a.Baseline.Channels.SequenceEqual(b.Baseline.Channels, StringComparer.OrdinalIgnoreCase) &&
        a.Processed.Channels.SequenceEqual(b.Processed.Channels, StringComparer.OrdinalIgnoreCase);

    private static PreprocessingPreviewWindowResponse MergeContiguousTiles(IReadOnlyList<PreprocessingPreviewWindowResponse> chain)
    {
        var first = chain[0];
        var last = chain[^1];
        var baseTimes = MergeChannelTimes(chain, t => t.Baseline.TraceTimesSeconds, t => t.Baseline.TracesUv, out var baseValues);
        var procTimes = MergeChannelTimes(chain, t => t.Processed.TraceTimesSeconds, t => t.Processed.TracesUv, out var procValues);
        var mergedValidity = MergeValidity(chain);
        return first with
        {
            StartSample = first.StartSample,
            EndSample = last.EndSample,
            StartSeconds = first.StartSeconds,
            EndSeconds = last.EndSeconds,
            Baseline = first.Baseline with { TraceTimesSeconds = baseTimes, TracesUv = baseValues },
            Processed = first.Processed with
            {
                TraceTimesSeconds = procTimes,
                TracesUv = procValues,
                IcaModelReused = chain.Any(t => t.Processed.IcaModelReused),
                ArtifactRemovalValidity = mergedValidity,
            },
        };
    }

    private static Dictionary<string, IReadOnlyList<double>> MergeChannelTimes(
        IReadOnlyList<PreprocessingPreviewWindowResponse> chain,
        Func<PreprocessingPreviewWindowResponse, IReadOnlyDictionary<string, IReadOnlyList<double>>> getTimes,
        Func<PreprocessingPreviewWindowResponse, IReadOnlyDictionary<string, IReadOnlyList<double?>>> getValues,
        out Dictionary<string, IReadOnlyList<double?>> mergedValues)
    {
        var mergedTimes = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        mergedValues = new Dictionary<string, IReadOnlyList<double?>>(StringComparer.Ordinal);
        foreach (var ch in getTimes(chain[0]).Keys)
        {
            var xs = new List<double>();
            var ys = new List<double?>();
            var lastX = double.NegativeInfinity;
            foreach (var tile in chain)
            {
                if (!getTimes(tile).TryGetValue(ch, out var tileXs) || !getValues(tile).TryGetValue(ch, out var tileYs))
                    continue;
                var count = Math.Min(tileXs.Count, tileYs.Count);
                for (var i = 0; i < count; i++)
                {
                    var x = tileXs[i];
                    if (x <= lastX) continue;
                    xs.Add(x);
                    ys.Add(tileYs[i]);
                    lastX = x;
                }
            }
            mergedTimes[ch] = xs;
            mergedValues[ch] = ys;
        }
        return mergedTimes;
    }

    private static ArtifactRemovalValidity? MergeValidity(IReadOnlyList<PreprocessingPreviewWindowResponse> chain)
    {
        var validities = chain.Select(t => t.Processed.ArtifactRemovalValidity).Where(v => v is not null).ToList();
        if (validities.Count == 0) return null;
        var intervals = validities
            .SelectMany(v => v!.Intervals)
            .GroupBy(i => (i.StartSample, i.EndSample, i.Reason))
            .Select(g => g.First())
            .OrderBy(i => i.StartSample)
            .ToArray();
        var first = validities[0]!;
        return first with
        {
            StartSample = chain[0].StartSample,
            EndSample = chain[^1].EndSample,
            Intervals = intervals,
            UnavailableSampleCount = validities.Sum(v => v!.UnavailableSampleCount),
            OutputUnavailableChannelSamples = validities.Sum(v => v!.OutputUnavailableChannelSamples),
            AdditionalUnavailableChannelSamples = validities.Sum(v => v!.AdditionalUnavailableChannelSamples),
        };
    }
}

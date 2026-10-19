using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrainPlatform.Desktop.Modules.Review.Cache;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

internal sealed record ReviewDisplayLevelReadResult(
    string LevelIdentity,
    int ChannelIndex,
    IReadOnlyList<ReviewDisplayLevelChunk> Chunks);

/// <summary>
/// Resolves only complete, identity-matching display chunks. A missing or
/// inconsistent index/chunk returns a cache miss for the caller to fall back.
/// </summary>
internal sealed class ReviewDisplayLevelReader
{
    private readonly ReviewDisplayLevelIndex index;
    private readonly ReviewDisplayLevelChunkCache chunks;

    public ReviewDisplayLevelReader(
        ReviewDisplayLevelIndex? index = null,
        ReviewDisplayLevelChunkCache? chunks = null)
    {
        this.index = index ?? new ReviewDisplayLevelIndex();
        this.chunks = chunks ?? new ReviewDisplayLevelChunkCache();
    }

    public async Task<ReviewDisplayLevelReadResult?> TryReadAsync(
        ReviewDisplayLevel level,
        int channelIndex,
        long firstSampleCounter,
        int sampleCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (channelIndex < 0 || channelIndex >= level.ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (firstSampleCounter < 0)
            throw new ArgumentOutOfRangeException(nameof(firstSampleCounter));
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        var endExclusive = checked(firstSampleCounter + sampleCount);
        var indexKey = new ReviewDisplayLevelIndexKey(level.Identity, channelIndex);
        var entries = await index.TryReadAsync(indexKey, cancellationToken);
        if (entries is null) return null;

        var selected = entries.Where(entry =>
                entry.FirstSampleCounter < endExclusive &&
                checked(entry.FirstSampleCounter + entry.SampleCount) > firstSampleCounter)
            .OrderBy(entry => entry.FirstSampleCounter)
            .ToArray();
        if (selected.Length == 0) return null;

        var coveredUntil = firstSampleCounter;
        foreach (var entry in selected)
        {
            if (entry.FirstSampleCounter > coveredUntil) return null;
            coveredUntil = Math.Max(coveredUntil, checked(entry.FirstSampleCounter + entry.SampleCount));
        }
        if (coveredUntil < endExclusive) return null;

        var output = new List<ReviewDisplayLevelChunk>(selected.Length);
        foreach (var entry in selected)
        {
            var chunkKey = new ReviewDisplayLevelChunkKey(level.Identity, entry.ChunkIndex, channelIndex);
            if (!string.Equals(entry.ChunkFingerprint, chunkKey.Fingerprint, StringComparison.Ordinal))
                return null;

            var chunk = await chunks.TryReadAsync(chunkKey, cancellationToken);
            if (chunk is null || chunk.Buckets.Count != entry.BucketCount ||
                chunk.Buckets.Count == 0 || chunk.Buckets[0].FirstSampleCounter != entry.FirstSampleCounter)
                return null;
            output.Add(chunk);
        }

        return new ReviewDisplayLevelReadResult(level.Identity, channelIndex, output);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Montages;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>
/// Publishes display chunks first and the level index last. An interrupted
/// publication therefore remains invisible to the display-level reader.
/// </summary>
internal sealed class ReviewDisplayLevelPublisher
{
    private readonly ReviewDisplayLevelChunkCache chunkCache;
    private readonly ReviewDisplayLevelIndex index;

    public ReviewDisplayLevelPublisher(
        ReviewDisplayLevelChunkCache chunkCache,
        ReviewDisplayLevelIndex index)
    {
        this.chunkCache = chunkCache;
        this.index = index;
    }

    public async Task PublishAsync(
        ReviewDisplayLevel level,
        int channelIndex,
        IReadOnlyList<ReviewDisplayLevelPublicationChunk> publications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(publications);
        if (channelIndex < 0 || channelIndex >= level.ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        if (publications.Count == 0)
            throw new ArgumentException("At least one display chunk is required.", nameof(publications));

        var entries = new List<ReviewDisplayLevelIndexEntry>(publications.Count);
        foreach (var publication in publications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = ReviewDisplayLevelChunkGenerator.Build(level, channelIndex,
                publication.ChunkIndex, publication.FirstSampleCounter, publication.SampleCount,
                publication.Segments);
            var key = new ReviewDisplayLevelChunkKey(level.Identity, publication.ChunkIndex, channelIndex);
            await chunkCache.StoreAsync(key, chunk, cancellationToken);
            entries.Add(new ReviewDisplayLevelIndexEntry(
                publication.ChunkIndex,
                chunk.Buckets[0].FirstSampleCounter,
                checked(chunk.Buckets.Sum(bucket => bucket.SampleCount)),
                chunk.Buckets.Count,
                key.Fingerprint));
        }

        entries.Sort((left, right) => left.ChunkIndex.CompareTo(right.ChunkIndex));
        await index.StoreAsync(new ReviewDisplayLevelIndexKey(level.Identity, channelIndex),
            entries, cancellationToken);
    }

    /// <summary>
    /// Streams projected source chunks once and publishes every channel's
    /// index only after all of its chunks are complete. Only index metadata is
    /// retained in memory; signal arrays stay bounded to one source chunk.
    /// </summary>
    public async Task PublishAllChannelsAsync(
        ReviewDisplayLevel level,
        IAsyncEnumerable<ReviewDisplayLevelPublicationChunk> publications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(publications);

        var entries = Enumerable.Range(0, level.ChannelCount)
            .ToDictionary(channel => channel, _ => new List<ReviewDisplayLevelIndexEntry>());
        var seen = 0;
        await foreach (var publication in publications.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var channelIndex = 0; channelIndex < level.ChannelCount; channelIndex++)
            {
                var chunk = ReviewDisplayLevelChunkGenerator.Build(level, channelIndex,
                    publication.ChunkIndex, publication.FirstSampleCounter,
                    publication.SampleCount, publication.Segments);
                var key = new ReviewDisplayLevelChunkKey(level.Identity, publication.ChunkIndex, channelIndex);
                await chunkCache.StoreAsync(key, chunk, cancellationToken);
                entries[channelIndex].Add(new ReviewDisplayLevelIndexEntry(
                    publication.ChunkIndex,
                    chunk.Buckets[0].FirstSampleCounter,
                    checked(chunk.Buckets.Sum(bucket => bucket.SampleCount)),
                    chunk.Buckets.Count,
                    key.Fingerprint));
            }
            seen++;
        }

        if (seen == 0)
            throw new ArgumentException("At least one display chunk is required.", nameof(publications));

        foreach (var (channelIndex, channelEntries) in entries)
        {
            channelEntries.Sort((left, right) => left.ChunkIndex.CompareTo(right.ChunkIndex));
            await index.StoreAsync(new ReviewDisplayLevelIndexKey(level.Identity, channelIndex),
                channelEntries, cancellationToken);
        }
    }
}

internal sealed record ReviewDisplayLevelPublicationChunk(
    int ChunkIndex,
    long FirstSampleCounter,
    int SampleCount,
    IReadOnlyList<ProjectedMontageSegment> Segments);

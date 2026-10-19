using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelReaderTests
{
    [Fact]
    public async Task Reads_only_complete_identity_matching_chunks()
    {
        var root = NewRoot();
        try
        {
            var level = new ReviewDisplayLevel("filtered-source", 4, 1, 1, 1, 2);
            var cache = new ReviewDisplayLevelChunkCache(root);
            var index = new ReviewDisplayLevelIndex(root);
            var firstKey = new ReviewDisplayLevelChunkKey(level.Identity, 0, 0);
            var secondKey = new ReviewDisplayLevelChunkKey(level.Identity, 1, 0);
            var firstChunk = new ReviewDisplayLevelChunk(level.Identity, 0,
                [new(0, 2, false, 0, 1, 1, 2)]);
            var secondChunk = new ReviewDisplayLevelChunk(level.Identity, 1,
                [new(2, 2, false, 2, 3, 3, 4)]);
            await cache.StoreAsync(firstKey, firstChunk, CancellationToken.None);
            await cache.StoreAsync(secondKey, secondChunk, CancellationToken.None);
            await index.StoreAsync(new ReviewDisplayLevelIndexKey(level.Identity),
                [new(0, 0, 2, 1, firstKey.Fingerprint), new(1, 2, 2, 1, secondKey.Fingerprint)],
                CancellationToken.None);

            var result = await new ReviewDisplayLevelReader(index, cache)
                .TryReadAsync(level, 0, 0, 4, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Chunks.Count);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Missing_chunk_or_index_gap_is_a_cache_miss()
    {
        var root = NewRoot();
        try
        {
            var level = new ReviewDisplayLevel("raw-source", 4, 1, 1, 1, 2);
            var cache = new ReviewDisplayLevelChunkCache(root);
            var index = new ReviewDisplayLevelIndex(root);
            var firstKey = new ReviewDisplayLevelChunkKey(level.Identity, 0);
            await cache.StoreAsync(firstKey,
                new(level.Identity, 0, [new(0, 2, false, 0, 1, 1, 2)]), CancellationToken.None);
            await index.StoreAsync(new ReviewDisplayLevelIndexKey(level.Identity),
                [new(0, 0, 2, 1, firstKey.Fingerprint), new(2, 4, 2, 1, "future")],
                CancellationToken.None);

            var result = await new ReviewDisplayLevelReader(index, cache)
                .TryReadAsync(level, 0, 0, 6, CancellationToken.None);

            Assert.Null(result);
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "brain-review-display-reader", Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

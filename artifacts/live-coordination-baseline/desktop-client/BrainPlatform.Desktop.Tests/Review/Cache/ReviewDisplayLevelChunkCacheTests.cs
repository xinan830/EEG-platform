namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewDisplayLevelChunkCacheTests
{
    [Fact]
    public async Task Completed_chunk_round_trips_extrema_and_explicit_gap()
    {
        var root = NewRoot();
        try
        {
            var level = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "filtered-a");
            var key = new ReviewDisplayLevelChunkKey(level.Identity, 4, 1);
            var chunk = new ReviewDisplayLevelChunk(level.Identity, 4,
                [new(100, 8, false, 100, 103, -2e-6, 3e-6), new(108, 2, true, -1, -1, double.NaN, double.NaN)]);
            var cache = new ReviewDisplayLevelChunkCache(root);

            await cache.StoreAsync(key, chunk, CancellationToken.None);
            var result = await cache.TryReadAsync(key, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(chunk.LevelIdentity, result!.LevelIdentity);
            Assert.Equal(chunk.ChunkIndex, result.ChunkIndex);
            Assert.Equal(chunk.Buckets.ToArray(), result.Buckets.ToArray());
            Assert.True(result.Buckets[1].IsGap);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Different_level_identity_is_a_cache_miss()
    {
        var root = NewRoot();
        try
        {
            var first = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "raw-a");
            var second = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "filtered-a");
            var cache = new ReviewDisplayLevelChunkCache(root);
            await cache.StoreAsync(new(first.Identity, 0),
                new(first.Identity, 0, [new(0, 1, false, 0, 0, 0, 1)]), CancellationToken.None);

            Assert.Null(await cache.TryReadAsync(new(second.Identity, 0), CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Corrupt_chunk_is_a_miss_and_does_not_fabricate_gap_data()
    {
        var root = NewRoot();
        try
        {
            var level = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "raw-a");
            var cache = new ReviewDisplayLevelChunkCache(root);
            await cache.StoreAsync(new(level.Identity, 0),
                new(level.Identity, 0, [new(0, 1, false, 0, 0, 0, 1)]), CancellationToken.None);
            var path = Directory.EnumerateFiles(root, "*.bprvd", SearchOption.AllDirectories).Single();
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            Assert.Null(await cache.TryReadAsync(new(level.Identity, 0), CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Display_chunks_are_evicted_when_the_persistent_budget_is_exceeded()
    {
        var probeRoot = NewRoot();
        var root = NewRoot();
        try
        {
            var level = ReviewDisplayLevelSelector.Select(60, 250, 800, 1, "raw-budget");
            var probeKey = new ReviewDisplayLevelChunkKey(level.Identity, 0);
            var chunk = new ReviewDisplayLevelChunk(level.Identity, 0,
                [new(0, 1, false, 0, 0, 0, 1)]);
            var probe = new ReviewDisplayLevelChunkCache(probeRoot);
            await probe.StoreAsync(probeKey, chunk, CancellationToken.None);
            var entryLength = new FileInfo(Directory.EnumerateFiles(probeRoot, "*.bprvd", SearchOption.AllDirectories).Single()).Length;

            var cache = new ReviewDisplayLevelChunkCache(root, entryLength * 2 + 1);
            await cache.StoreAsync(new(level.Identity, 0), chunk, CancellationToken.None);
            await cache.StoreAsync(new(level.Identity, 1), chunk with { ChunkIndex = 1 }, CancellationToken.None);
            await cache.StoreAsync(new(level.Identity, 2), chunk with { ChunkIndex = 2 }, CancellationToken.None);

            Assert.Null(await cache.TryReadAsync(new(level.Identity, 0), CancellationToken.None));
            Assert.NotNull(await cache.TryReadAsync(new(level.Identity, 1), CancellationToken.None));
            Assert.NotNull(await cache.TryReadAsync(new(level.Identity, 2), CancellationToken.None));
        }
        finally
        {
            Delete(probeRoot);
            Delete(root);
        }
    }

    [Fact]
    public async Task Display_level_index_round_trips_only_sorted_completed_entries()
    {
        var root = NewRoot();
        try
        {
            var level = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "raw-a");
            var key = new ReviewDisplayLevelIndexKey(level.Identity, 1);
            var entries = new ReviewDisplayLevelIndexEntry[]
            {
                new(0, 0, 1024, 32, "chunk-a"),
                new(1, 1024, 1024, 32, "chunk-b")
            };
            var index = new ReviewDisplayLevelIndex(root);

            await index.StoreAsync(key, entries, CancellationToken.None);
            var result = await index.TryReadAsync(key, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(entries, result!.ToArray());
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Corrupt_display_level_index_is_a_miss()
    {
        var root = NewRoot();
        try
        {
            var level = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "raw-a");
            var key = new ReviewDisplayLevelIndexKey(level.Identity);
            var index = new ReviewDisplayLevelIndex(root);
            await index.StoreAsync(key,
                [new(0, 0, 1024, 32, "chunk-a")], CancellationToken.None);
            var path = Directory.EnumerateFiles(root, "*.bprvdi", SearchOption.AllDirectories).Single();
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            Assert.Null(await index.TryReadAsync(key, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "brain-review-display-cache", Guid.NewGuid().ToString("N"));
    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}


namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewFilteredSourceChunkCacheTests
{
    [Fact]
    public async Task CompletedChunkCanBeReadOnlyByItsExactFilterContractFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-review-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ReviewFilteredSourceChunkCache(root);
            var key = Key(new ReviewFilterContract("display-iir-sos-v2", "contract-a"));
            var window = Window();

            await cache.StoreAsync(key, window, CancellationToken.None);

            var hit = await cache.TryReadAsync(key, CancellationToken.None);
            var changedContract = await cache.TryReadAsync(
                Key(new ReviewFilterContract("display-iir-sos-v3", "contract-b")), CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal(window.Segments[0].SampleMajorValues, hit!.Segments[0].SampleMajorValues);
            Assert.Null(changedContract);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompletedChunkPersistsItsCausalCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-review-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ReviewFilteredSourceChunkCache(root);
            var key = Key(new ReviewFilterContract("display-iir-sos-v2", "contract-a"));
            await cache.StoreAsync(key, Window(), "checkpoint-v1", CancellationToken.None);

            var hit = await cache.TryReadAsync(key, CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal("checkpoint-v1", hit!.Checkpoint);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledWriteDoesNotCreateReadableChunk()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-review-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ReviewFilteredSourceChunkCache(root);
            var key = Key(new ReviewFilterContract("display-iir-sos-v2", "contract-a"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                cache.StoreAsync(key, Window(), cancellation.Token));

            Assert.Null(await cache.TryReadAsync(key, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ReviewFilteredSourceChunkKey Key(ReviewFilterContract contract) => new(
        Guid.Parse("712a5e5a-9d4e-4d36-b1ef-9a5254a4d9bb"),
        "raw-manifest", 1000, "schema",
        new RecordingReviewFilterSettings(1, 30, 50), contract,
        0, 10_000, 0, "recorded-segments-v1;causal-state-never-crosses-gap");

    private static RecordingReviewWindow Window() => new(0, 0, 0.002,
        [new RecordingReviewSegment(0, 2, 2, [1e-6, 2e-6, 3e-6, 4e-6])]);
}

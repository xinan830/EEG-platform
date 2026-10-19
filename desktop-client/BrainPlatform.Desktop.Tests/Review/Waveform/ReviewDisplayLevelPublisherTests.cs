using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelPublisherTests
{
    [Fact]
    public async Task Publishes_chunks_before_exposing_the_complete_index()
    {
        var root = NewRoot();
        try
        {
            var level = new ReviewDisplayLevel("filtered-source", 4, 1, 800, 1, 2);
            var cache = new ReviewDisplayLevelChunkCache(root);
            var index = new ReviewDisplayLevelIndex(root);
            var publisher = new ReviewDisplayLevelPublisher(cache, index);
            var publications = new[]
            {
                new ReviewDisplayLevelPublicationChunk(0, 0, 2,
                    [new ProjectedMontageSegment(0, 2,
                        [new ProjectedMontageChannel("F3", [1, 2])])]),
                new ReviewDisplayLevelPublicationChunk(1, 2, 2,
                    [new ProjectedMontageSegment(2, 2,
                        [new ProjectedMontageChannel("F3", [3, 4])])])
            };

            await publisher.PublishAsync(level, 0, publications, CancellationToken.None);
            var result = await new ReviewDisplayLevelReader(index, cache)
                .TryReadAsync(level, 0, 0, 4, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Chunks.Count);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Cancellation_before_publication_does_not_create_an_index()
    {
        var root = NewRoot();
        try
        {
            var level = new ReviewDisplayLevel("raw-source", 4, 1, 800, 1, 2);
            var index = new ReviewDisplayLevelIndex(root);
            var publisher = new ReviewDisplayLevelPublisher(
                new ReviewDisplayLevelChunkCache(root), index);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => publisher.PublishAsync(level, 0,
                [new ReviewDisplayLevelPublicationChunk(0, 0, 2,
                    [new ProjectedMontageSegment(0, 2,
                        [new ProjectedMontageChannel("F3", [1, 2])])])], cancellation.Token));

            Assert.Null(await index.TryReadAsync(new ReviewDisplayLevelIndexKey(level.Identity),
                CancellationToken.None));
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "brain-review-display-publisher", Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

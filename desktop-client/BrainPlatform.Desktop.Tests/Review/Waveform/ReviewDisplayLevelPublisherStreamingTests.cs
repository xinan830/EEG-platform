using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelPublisherStreamingTests
{
    [Fact]
    public async Task Publishes_all_channel_indices_after_streaming_chunks()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-review-display-streaming", Guid.NewGuid().ToString("N"));
        try
        {
            var level = new ReviewDisplayLevel("raw-source", 4, 2, 800, 1, 1);
            var index = new ReviewDisplayLevelIndex(root);
            var publisher = new ReviewDisplayLevelPublisher(
                new ReviewDisplayLevelChunkCache(root), index);

            async IAsyncEnumerable<ReviewDisplayLevelPublicationChunk> Chunks()
            {
                for (var chunkIndex = 0; chunkIndex < 2; chunkIndex++)
                {
                    var first = chunkIndex * 2L;
                    yield return new ReviewDisplayLevelPublicationChunk(chunkIndex, first, 2,
                        [new ProjectedMontageSegment(first, 2,
                            [new ProjectedMontageChannel("F3", [1, 2]),
                             new ProjectedMontageChannel("F7", [3, 4])])]);
                    await Task.Yield();
                }
            }

            await publisher.PublishAllChannelsAsync(level, Chunks(), CancellationToken.None);

            for (var channelIndex = 0; channelIndex < level.ChannelCount; channelIndex++)
            {
                var entries = await index.TryReadAsync(
                    new ReviewDisplayLevelIndexKey(level.Identity, channelIndex), CancellationToken.None);
                Assert.NotNull(entries);
                Assert.Equal(2, entries!.Count);
                Assert.Equal(0, entries[0].ChunkIndex);
                Assert.Equal(1, entries[1].ChunkIndex);
                Assert.Equal(0, entries[0].FirstSampleCounter);
                Assert.Equal(2, entries[1].FirstSampleCounter);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_source_chunks_with_a_different_display_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-review-display-streaming", Guid.NewGuid().ToString("N"));
        try
        {
            var level = new ReviewDisplayLevel("filtered-source", 4, 1, 800, 1, 1);
            var index = new ReviewDisplayLevelIndex(root);
            var publisher = new ReviewDisplayLevelPublisher(
                new ReviewDisplayLevelChunkCache(root), index);

            async IAsyncEnumerable<ReviewDisplayLevelSourceChunk> Sources()
            {
                yield return new ReviewDisplayLevelSourceChunk("raw-source", 0, 0, 1,
                    [new ProjectedMontageSegment(0, 1,
                        [new ProjectedMontageChannel("F3", [1])])]);
                await Task.Yield();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                publisher.PublishAllChannelsFromSourceAsync(level, Sources(), CancellationToken.None));
            Assert.Null(await index.TryReadAsync(
                new ReviewDisplayLevelIndexKey(level.Identity), CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Streaming_publication_requests_one_source_chunk_at_a_time()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-review-display-streaming", Guid.NewGuid().ToString("N"));
        try
        {
            var level = new ReviewDisplayLevel("raw-source", 4, 1, 800, 1, 1);
            var publisher = new ReviewDisplayLevelPublisher(
                new ReviewDisplayLevelChunkCache(root), new ReviewDisplayLevelIndex(root));
            var active = 0;
            var maximumActive = 0;

            async IAsyncEnumerable<ReviewDisplayLevelSourceChunk> Sources()
            {
                for (var chunkIndex = 0; chunkIndex < 64; chunkIndex++)
                {
                    var current = Interlocked.Increment(ref active);
                    Interlocked.Exchange(ref maximumActive, Math.Max(maximumActive, current));
                    yield return new ReviewDisplayLevelSourceChunk("raw-source", chunkIndex, chunkIndex, 1,
                        [new ProjectedMontageSegment(chunkIndex, 1,
                            [new ProjectedMontageChannel("F3", [1])])]);
                    Interlocked.Decrement(ref active);
                    await Task.Yield();
                }
            }

            await publisher.PublishAllChannelsFromSourceAsync(level, Sources(), CancellationToken.None);

            Assert.Equal(1, maximumActive);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

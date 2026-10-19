using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Playback;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelProviderTests
{
    [Fact]
    public async Task Uses_complete_level_and_preserves_recording_time()
    {
        var root = NewRoot();
        try
        {
            var cache = new ReviewDisplayLevelChunkCache(root);
            var index = new ReviewDisplayLevelIndex(root);
            var level = ReviewDisplayLevelSelector.Select(1, 4, 800, 1, "raw-source");
            await new ReviewDisplayLevelPublisher(cache, index).PublishAsync(level, 0,
                [new ReviewDisplayLevelPublicationChunk(0, 0, 4,
                    [new ProjectedMontageSegment(0, 4,
                        [new ProjectedMontageChannel("F3", [1, -2, 3, 4])])])],
                CancellationToken.None);

            var frame = new RecordingReviewFrame(
                Guid.NewGuid(), 7, 4, 0, 10, 11, "REF",
                [new ProjectedMontageSegment(0, 4,
                    [new ProjectedMontageChannel("F3", [1, -2, 3, 4])])],
                ["F3"], ["#123456"], "raw-source");
            var provider = new ReviewDisplayLevelProvider(new ReviewDisplayLevelReader(index, cache));

            var display = await provider.TryReadAsync(frame, 10, 1, 800, CancellationToken.None);

            Assert.NotNull(display);
            Assert.Equal([10d, 10.25d, 10.5d, 10.75d], display!.Traces[0].XSeconds);
            Assert.Equal([1d, -2d, 3d, 4d], display.Traces[0].ValuesVolts);
            Assert.Equal(frame.OutputChannelNames, display.OutputChannelNames);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Missing_level_returns_cache_miss_without_fabricating_trace_data()
    {
        var root = NewRoot();
        try
        {
            var frame = new RecordingReviewFrame(
                Guid.NewGuid(), 1, 4, 0, 0, 1, "REF",
                [new ProjectedMontageSegment(0, 4,
                    [new ProjectedMontageChannel("F3", [1, 2, 3, 4])])],
                ["F3"], DisplaySourceIdentity: "raw-source");
            var provider = new ReviewDisplayLevelProvider(new ReviewDisplayLevelReader(
                new ReviewDisplayLevelIndex(root), new ReviewDisplayLevelChunkCache(root)));

            var display = await provider.TryReadAsync(frame, 0, 1, 800, CancellationToken.None);

            Assert.Null(display);
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(),
        "brain-review-display-provider", Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelChunkGeneratorTests
{
    [Fact]
    public void Builds_extrema_in_time_order_and_preserves_segment_gap()
    {
        var level = new ReviewDisplayLevel("filtered-source", 4, 1, 1, 4, 2);
        var segments = new[]
        {
            new ProjectedMontageSegment(0, 2,
                [new ProjectedMontageChannel("F3", [1, 5])]),
            new ProjectedMontageSegment(4, 2,
                [new ProjectedMontageChannel("F3", [3, 4])])
        };

        var chunk = ReviewDisplayLevelChunkGenerator.Build(level, 0, 2, 0, 6, segments);

        Assert.Collection(chunk.Buckets,
            first =>
            {
                Assert.False(first.IsGap);
                Assert.Equal(1, first.MinVolts);
                Assert.Equal(5, first.MaxVolts);
            },
            second => Assert.True(second.IsGap),
            third =>
            {
                Assert.False(third.IsGap);
                Assert.Equal(3, third.MinVolts);
                Assert.Equal(4, third.MaxVolts);
            });
    }

    [Fact]
    public void Any_invalid_sample_invalidates_its_display_bucket()
    {
        var level = new ReviewDisplayLevel("raw-source", 4, 1, 1, 1, 2);
        var segments = new[]
        {
            new ProjectedMontageSegment(0, 4,
                [new ProjectedMontageChannel("F3", [1, double.NaN, 3, 4])])
        };

        var chunk = ReviewDisplayLevelChunkGenerator.Build(level, 0, 0, 0, 4, segments);

        Assert.Equal(2, chunk.Buckets.Count);
        Assert.True(chunk.Buckets[0].IsGap);
        Assert.True(double.IsNaN(chunk.Buckets[0].MinVolts));
    }

    [Fact]
    public void A_gap_inside_one_bucket_is_not_bridged_by_extrema()
    {
        var level = new ReviewDisplayLevel("raw-source", 4, 1, 1, 1, 4);
        var segments = new[]
        {
            new ProjectedMontageSegment(0, 1,
                [new ProjectedMontageChannel("F3", [1])]),
            new ProjectedMontageSegment(2, 2,
                [new ProjectedMontageChannel("F3", [3, 4])])
        };

        var chunk = ReviewDisplayLevelChunkGenerator.Build(level, 0, 0, 0, 4, segments);

        Assert.Single(chunk.Buckets);
        Assert.True(chunk.Buckets[0].IsGap);
    }

    [Fact]
    public void Channel_index_is_part_of_display_chunk_identity()
    {
        var level = ReviewDisplayLevelSelector.Select(1, 4, 1, 2, "raw-source");
        var first = new ReviewDisplayLevelChunkKey(level.Identity, 0, 0);
        var second = new ReviewDisplayLevelChunkKey(level.Identity, 0, 1);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }
}

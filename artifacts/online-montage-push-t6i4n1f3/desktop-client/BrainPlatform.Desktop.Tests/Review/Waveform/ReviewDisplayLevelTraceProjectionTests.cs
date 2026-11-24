using BrainPlatform.Desktop.Modules.Review.Cache;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplayLevelTraceProjectionTests
{
    [Fact]
    public void Emits_extrema_in_sample_time_order_and_keeps_gap_breaks()
    {
        var level = new ReviewDisplayLevel("raw-source", 2, 1, 800, 2, 4);
        var chunk = new ReviewDisplayLevelChunk(level.Identity, 0,
            [new(0, 4, false, 3, 1, -2, 4), new(4, 2, true, -1, -1, double.NaN, double.NaN)]);

        var result = ReviewDisplayLevelTraceProjection.Build(level, [chunk], 10);

        Assert.Equal([10.5, 11.5, 12], result.XSeconds);
        Assert.Equal([4d, -2d, double.NaN], result.ValuesVolts);
    }

    [Fact]
    public void Rejects_a_chunk_from_another_display_identity()
    {
        var level = new ReviewDisplayLevel("raw-source", 2, 1, 800, 2, 4);
        var other = new ReviewDisplayLevelChunk("filtered-source", 0,
            [new(0, 4, false, 0, 1, 1, 2)]);

        Assert.Throws<ArgumentException>(() =>
            ReviewDisplayLevelTraceProjection.Build(level, [other], 0));
    }
}

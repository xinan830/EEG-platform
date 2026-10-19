namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewDisplayLevelSelectorTests
{
    [Fact]
    public void Wide_short_view_selects_fine_power_of_two_buckets()
    {
        var level = ReviewDisplayLevelSelector.Select(10, 1000, 1000, 2, "filtered-a");

        Assert.Equal(8, level.SamplesPerBucket);
        Assert.True(level.SamplesPerBucket > 0);
        Assert.Equal("filtered-a", level.SourceFingerprint);
    }

    [Fact]
    public void Wider_time_range_selects_no_finer_level_than_short_view()
    {
        var shortView = ReviewDisplayLevelSelector.Select(10, 1000, 1000, 2, "raw-a");
        var longView = ReviewDisplayLevelSelector.Select(86_400, 1000, 1000, 2, "raw-a");

        Assert.True(longView.SamplesPerBucket >= shortView.SamplesPerBucket);
        Assert.True(longView.MaximumPointsPerTrace <= 2_000);
    }

    [Fact]
    public void Source_and_channel_identity_are_part_of_the_level_key()
    {
        var raw = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "raw-a");
        var filtered = ReviewDisplayLevelSelector.Select(60, 250, 800, 2, "filtered-a");
        var threeChannels = ReviewDisplayLevelSelector.Select(60, 250, 800, 3, "raw-a");

        Assert.NotEqual(raw.Identity, filtered.Identity);
        Assert.NotEqual(raw.Identity, threeChannels.Identity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_geometry_is_rejected(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReviewDisplayLevelSelector.Select(10, 250, width, 2, "raw-a"));
    }
}

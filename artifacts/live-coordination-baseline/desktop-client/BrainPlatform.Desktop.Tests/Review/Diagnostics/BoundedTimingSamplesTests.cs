using BrainPlatform.Desktop.Modules.Review.Diagnostics;

namespace BrainPlatform.Desktop.Tests.Review.Diagnostics;

public sealed class BoundedTimingSamplesTests
{
    [Fact]
    public void Summarize_uses_nearest_rank_percentiles_for_current_samples()
    {
        var samples = new BoundedTimingSamples(5);
        foreach (var value in new[] { 50d, 10d, 40d, 20d, 30d }) samples.Add(value);

        var summary = samples.Summarize();

        Assert.Equal(5, summary.Count);
        Assert.Equal(30, summary.P50);
        Assert.Equal(50, summary.P95);
        Assert.Equal(50, summary.P99);
    }

    [Fact]
    public void Summarize_keeps_only_newest_samples_after_capacity_is_reached()
    {
        var samples = new BoundedTimingSamples(3);
        foreach (var value in new[] { 100d, 2d, 3d, 4d }) samples.Add(value);

        var summary = samples.Summarize();

        Assert.Equal(3, summary.Count);
        Assert.Equal(3, summary.P50);
        Assert.Equal(4, summary.P95);
        Assert.Equal(4, summary.P99);
    }

    [Fact]
    public void Clear_removes_samples_and_invalid_values_are_rejected()
    {
        var samples = new BoundedTimingSamples(2);
        samples.Add(1);
        samples.Clear();

        Assert.Equal(new TimingPercentiles(0, 0, 0, 0), samples.Summarize());
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Add(double.NaN));
    }
}

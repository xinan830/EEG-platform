using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TrendSeriesAppendPlannerTests
{
    [Fact]
    public void AppendedHistoryReturnsOnlyTheNewSuffixIndex()
    {
        var previous = new[] { (1d, 2d), (2d, 3d) };
        var next = new[] { (1d, 2d), (2d, 3d), (3d, 4d) };

        Assert.Equal(2, TrendSeriesAppendPlanner.FindAppendStart(previous, next));
    }

    [Fact]
    public void RewindOrChangedHistoryRequestsReplacement()
    {
        var previous = new[] { (1d, 2d), (2d, 3d), (3d, 4d) };

        Assert.Equal(-1, TrendSeriesAppendPlanner.FindAppendStart(previous, previous[..2]));
        Assert.Equal(-1, TrendSeriesAppendPlanner.FindAppendStart(previous,
            [(1d, 2d), (2d, 99d), (3d, 4d)]));
    }

    [Fact]
    public void UnchangedHistoryDoesNotAppendOrReplace()
    {
        var previous = new[] { (1d, 2d), (2d, 3d) };

        Assert.Equal(previous.Length, TrendSeriesAppendPlanner.FindAppendStart(previous, previous.ToArray()));
    }
}

using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmListViewModelTests
{
    [Fact]
    public void StaticRange_RejectsReversedRange()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("10", "2", 20));
    }

    [Fact]
    public void StaticRange_RejectsRangeOutsideRegisteredRecording()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("0", "20.001", 20));
    }

    [Fact]
    public void StaticRange_RejectsNonFiniteInput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AlgorithmListViewModel.ParseStaticRangeOrThrow("NaN", "10", 20));
    }

    [Fact]
    public void StaticRange_PreservesExplicitSeconds()
    {
        var range = AlgorithmListViewModel.ParseStaticRangeOrThrow("1.250", "9.875", 20);

        Assert.Equal(new TimeRange(1.25, 9.875), range);
    }
}

using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class SweepEraseBandAnimatorTests
{
    [Fact]
    public void Animation_InterpolatesOnlyBetweenReceivedPositions()
    {
        var animator = new SweepEraseBandAnimator();
        Assert.Equal(0.2d, animator.SetTarget(0, 0.2d, 10, 0));

        Assert.Equal(0.2d, animator.SetTarget(0, 0.25d, 10, 10));
        Assert.True(animator.TryAdvance(35, out var intermediate));
        Assert.InRange(intermediate, 0.2d, 0.25d);
        Assert.True(animator.TryAdvance(100, out var final));
        Assert.Equal(0.25d, final, 6);
    }

    [Fact]
    public void NewPage_SnapsInsteadOfAnimatingBackwardAcrossThePage()
    {
        var animator = new SweepEraseBandAnimator();
        animator.SetTarget(0, 9.8d, 10, 0);

        var cursor = animator.SetTarget(10, 0.1d, 10, 10);

        Assert.Equal(0.1d, cursor, 6);
        Assert.False(animator.TryAdvance(30, out _));
    }
}


namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingPlaybackControllerTests
{
    [Fact]
    public void PlaybackUsesMonotonicClockAndPauseFreezesPosition()
    {
        var now = 100d;
        var controller = new RecordingPlaybackController(20, () => now);

        controller.Play();
        now = 102.5;
        controller.Update();
        Assert.Equal(2.5, controller.PositionSeconds, 6);

        controller.Pause();
        now = 109;
        controller.Update();
        Assert.Equal(2.5, controller.PositionSeconds, 6);
    }

    [Fact]
    public void SeekClampsAndPlaybackStopsAtEnd()
    {
        var now = 0d;
        var controller = new RecordingPlaybackController(5, () => now);

        controller.Seek(99);
        Assert.Equal(5, controller.PositionSeconds);
        controller.Seek(-1);
        Assert.Equal(0, controller.PositionSeconds);
        controller.Play();
        now = 6;
        controller.Update();

        Assert.Equal(5, controller.PositionSeconds);
        Assert.False(controller.IsPlaying);
        Assert.True(controller.IsCompleted);
    }

    [Fact]
    public void ChangingPlaybackRatePreservesCurrentPositionAndUsesTheNewRate()
    {
        var now = 0d;
        var controller = new RecordingPlaybackController(20, () => now);
        controller.Play();
        now = 2;
        controller.SetPlaybackRate(2);
        Assert.Equal(2, controller.PositionSeconds, 6);

        now = 3.5;
        controller.Update();
        Assert.Equal(5, controller.PositionSeconds, 6);
        Assert.Equal(2, controller.PlaybackRate);
    }
}

using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class WallClockSecondTickProviderTests
{
    [Fact]
    public void Major_ticks_follow_wall_clock_seconds_even_when_recording_starts_mid_second()
    {
        var origin = new DateTimeOffset(2026, 9, 24, 13, 21, 30, 387, TimeSpan.Zero);

        var ticks = WallClockSecondTickProvider.CalculateAlignedSeconds(0, 2, origin);

        Assert.Equal(2, ticks.Count);
        Assert.Equal(0.613, ticks[0], 6);
        Assert.Equal(1.613, ticks[1], 6);
        Assert.All(ticks, tick => Assert.Equal(0, origin.AddSeconds(tick).Millisecond));
    }
}

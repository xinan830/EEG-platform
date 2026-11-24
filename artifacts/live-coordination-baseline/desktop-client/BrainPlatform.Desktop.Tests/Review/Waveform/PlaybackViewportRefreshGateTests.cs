using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class PlaybackViewportRefreshGateTests
{
    [Fact]
    public void Playback_viewport_work_is_limited_to_thirty_updates_per_second()
    {
        var gate = new PlaybackViewportRefreshGate();
        var frameInterval = Stopwatch.Frequency / 60;

        Assert.True(gate.ShouldRefresh(isPlaying: true, timestamp: Stopwatch.Frequency));
        Assert.False(gate.ShouldRefresh(isPlaying: true, timestamp: Stopwatch.Frequency + frameInterval));
        Assert.True(gate.ShouldRefresh(isPlaying: true,
            timestamp: Stopwatch.Frequency + Stopwatch.Frequency / 30 + 1));
    }

    [Fact]
    public void Paused_navigation_is_immediate_and_resets_playback_throttle()
    {
        var gate = new PlaybackViewportRefreshGate();

        Assert.True(gate.ShouldRefresh(isPlaying: true, timestamp: Stopwatch.Frequency));
        Assert.True(gate.ShouldRefresh(isPlaying: false, timestamp: Stopwatch.Frequency + 1));
        Assert.True(gate.ShouldRefresh(isPlaying: true, timestamp: Stopwatch.Frequency + 2));
    }
}

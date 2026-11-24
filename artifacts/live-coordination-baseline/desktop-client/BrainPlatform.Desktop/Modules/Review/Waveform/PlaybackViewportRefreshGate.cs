using System.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

/// <summary>Limits expensive plot-range and marker layout work while keeping the playback cursor frame-synchronous.</summary>
internal sealed class PlaybackViewportRefreshGate
{
    private static readonly long MinimumIntervalTicks = Stopwatch.Frequency / 30;
    private long lastRefreshTimestamp;

    internal bool ShouldRefresh(bool isPlaying, long timestamp)
    {
        if (!isPlaying)
        {
            Reset();
            return true;
        }

        if (lastRefreshTimestamp != 0 && timestamp - lastRefreshTimestamp < MinimumIntervalTicks)
            return false;

        lastRefreshTimestamp = timestamp;
        return true;
    }

    internal void Reset() => lastRefreshTimestamp = 0;
}

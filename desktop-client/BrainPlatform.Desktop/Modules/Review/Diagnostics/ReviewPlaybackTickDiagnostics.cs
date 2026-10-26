using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Diagnostics;

/// <summary>Summarizes playback dispatcher cadence without logging on every tick.</summary>
public sealed class ReviewPlaybackTickDiagnostics
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);
    private readonly bool enabled = ReviewPerformanceDiagnostics.Enabled;
    private long previousTickTimestamp;
    private long reportStartedTimestamp;
    private int tickCount;
    private int gapOver33MillisecondsCount;
    private int gapOver50MillisecondsCount;
    private double maximumTickGapMilliseconds;
    private double maximumSynchronousTickMilliseconds;

    public void Reset() => previousTickTimestamp = 0;

    public void Record(long startedTimestamp, long completedTimestamp, double positionSeconds)
    {
        if (!enabled) return;

        var gapMilliseconds = previousTickTimestamp == 0
            ? 0
            : Stopwatch.GetElapsedTime(previousTickTimestamp, startedTimestamp).TotalMilliseconds;
        previousTickTimestamp = startedTimestamp;
        var workMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp, completedTimestamp).TotalMilliseconds;
        tickCount++;
        maximumTickGapMilliseconds = Math.Max(maximumTickGapMilliseconds, gapMilliseconds);
        maximumSynchronousTickMilliseconds = Math.Max(maximumSynchronousTickMilliseconds, workMilliseconds);
        if (gapMilliseconds > 33) gapOver33MillisecondsCount++;
        if (gapMilliseconds > 50) gapOver50MillisecondsCount++;

        if (reportStartedTimestamp == 0) reportStartedTimestamp = startedTimestamp;
        if (Stopwatch.GetElapsedTime(reportStartedTimestamp, completedTimestamp) < ReportInterval) return;

        DesktopLog.Info(string.Create(CultureInfo.InvariantCulture,
            $"review_playback_tick_perf ticks={tickCount}" +
            $" gap_over_33ms={gapOver33MillisecondsCount} gap_over_50ms={gapOver50MillisecondsCount}" +
            $" max_tick_gap_ms={maximumTickGapMilliseconds:0.0}" +
            $" max_tick_work_ms={maximumSynchronousTickMilliseconds:0.0}" +
            $" position_s={positionSeconds:0.######}"));
        reportStartedTimestamp = completedTimestamp;
        tickCount = 0;
        gapOver33MillisecondsCount = 0;
        gapOver50MillisecondsCount = 0;
        maximumTickGapMilliseconds = 0;
        maximumSynchronousTickMilliseconds = 0;
    }
}

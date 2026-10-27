using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Diagnostics;

/// <summary>Summarizes playback dispatcher cadence without logging on every tick.</summary>
public sealed class ReviewPlaybackTickDiagnostics
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);
    private const int MaximumSamplesPerInterval = 512;
    private readonly bool enabled = ReviewPerformanceDiagnostics.Enabled;
    private readonly BoundedTimingSamples tickGapSamples = new(MaximumSamplesPerInterval);
    private readonly BoundedTimingSamples tickWorkSamples = new(MaximumSamplesPerInterval);
    private long previousTickTimestamp;
    private long reportStartedTimestamp;
    private bool playbackSessionLogged;
    private int tickCount;
    private int gapOver33MillisecondsCount;
    private int gapOver50MillisecondsCount;
    private double maximumTickGapMilliseconds;
    private double maximumSynchronousTickMilliseconds;

    public void Reset()
    {
        previousTickTimestamp = 0;
        reportStartedTimestamp = 0;
        playbackSessionLogged = false;
        tickCount = 0;
        gapOver33MillisecondsCount = 0;
        gapOver50MillisecondsCount = 0;
        maximumTickGapMilliseconds = 0;
        maximumSynchronousTickMilliseconds = 0;
        tickGapSamples.Clear();
        tickWorkSamples.Clear();
    }

    public void Record(string recordingId, int samplingRateHz, string playbackSpeed,
        long startedTimestamp, long completedTimestamp, double positionSeconds)
    {
        if (!enabled) return;

        if (!playbackSessionLogged)
        {
            DesktopLog.Info(
                $"review_playback_tick_perf scenario={DesktopLog.PerformanceScenario} recording_id={recordingId}" +
                $" sampling_rate_hz={samplingRateHz.ToString(CultureInfo.InvariantCulture)}" +
                $" playback_speed={playbackSpeed} phase=started");
            playbackSessionLogged = true;
        }

        var hasPreviousTick = previousTickTimestamp != 0;
        var gapMilliseconds = !hasPreviousTick
            ? 0
            : Stopwatch.GetElapsedTime(previousTickTimestamp, startedTimestamp).TotalMilliseconds;
        previousTickTimestamp = startedTimestamp;
        var workMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp, completedTimestamp).TotalMilliseconds;
        tickCount++;
        if (hasPreviousTick) tickGapSamples.Add(gapMilliseconds);
        tickWorkSamples.Add(workMilliseconds);
        maximumTickGapMilliseconds = Math.Max(maximumTickGapMilliseconds, gapMilliseconds);
        maximumSynchronousTickMilliseconds = Math.Max(maximumSynchronousTickMilliseconds, workMilliseconds);
        if (gapMilliseconds > 33) gapOver33MillisecondsCount++;
        if (gapMilliseconds > 50) gapOver50MillisecondsCount++;

        if (reportStartedTimestamp == 0) reportStartedTimestamp = startedTimestamp;
        if (Stopwatch.GetElapsedTime(reportStartedTimestamp, completedTimestamp) < ReportInterval) return;

        var gapSummary = tickGapSamples.Summarize();
        var workSummary = tickWorkSamples.Summarize();

        DesktopLog.Info(
            $"review_playback_tick_perf scenario={DesktopLog.PerformanceScenario} recording_id={recordingId}" +
            $" sampling_rate_hz={samplingRateHz.ToString(CultureInfo.InvariantCulture)} playback_speed={playbackSpeed}" +
            $" ticks={tickCount.ToString(CultureInfo.InvariantCulture)}" +
            $" gap_over_33ms={gapOver33MillisecondsCount.ToString(CultureInfo.InvariantCulture)}" +
            $" gap_over_50ms={gapOver50MillisecondsCount.ToString(CultureInfo.InvariantCulture)}" +
            $" tick_gap_n={gapSummary.Count.ToString(CultureInfo.InvariantCulture)}" +
            $" tick_gap_p50_ms={gapSummary.P50.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" tick_gap_p95_ms={gapSummary.P95.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" tick_gap_p99_ms={gapSummary.P99.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" tick_work_n={workSummary.Count.ToString(CultureInfo.InvariantCulture)}" +
            $" tick_work_p50_ms={workSummary.P50.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" tick_work_p95_ms={workSummary.P95.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" tick_work_p99_ms={workSummary.P99.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" max_tick_gap_ms={maximumTickGapMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" max_tick_work_ms={maximumSynchronousTickMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}" +
            $" position_s={positionSeconds.ToString("0.######", CultureInfo.InvariantCulture)}");
        reportStartedTimestamp = completedTimestamp;
        tickCount = 0;
        gapOver33MillisecondsCount = 0;
        gapOver50MillisecondsCount = 0;
        maximumTickGapMilliseconds = 0;
        maximumSynchronousTickMilliseconds = 0;
        tickGapSamples.Clear();
        tickWorkSamples.Clear();
    }
}

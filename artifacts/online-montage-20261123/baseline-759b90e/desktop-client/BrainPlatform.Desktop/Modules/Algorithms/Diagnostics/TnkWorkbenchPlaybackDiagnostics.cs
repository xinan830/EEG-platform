using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

/// <summary>Bounded metadata-only timing summaries for TNK workbench playback.</summary>
internal sealed class TnkWorkbenchPlaybackDiagnostics
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);
    private readonly double[] tickGap = new double[512];
    private readonly double[] tickWork = new double[512];
    private readonly double[] positionUpdate = new double[512];
    private readonly double[] analysisSchedule = new double[512];
    private int sampleCount;
    private long previousTick;
    private long reportStarted;
    private int ticks;
    private int gapsOver33Ms;
    private int gapsOver50Ms;
    private double maxGapMs;
    private double maxWorkMs;
    private TimeSpan processCpuAtReportStart;

    internal void Reset()
    {
        previousTick = reportStarted = 0;
        ticks = gapsOver33Ms = gapsOver50Ms = 0;
        maxGapMs = maxWorkMs = 0;
        processCpuAtReportStart = TimeSpan.Zero;
        Array.Clear(tickGap);
        Array.Clear(tickWork);
        Array.Clear(positionUpdate);
        Array.Clear(analysisSchedule);
        sampleCount = 0;
    }

    internal void Record(string recordingId, double samplingRateHz, double speed,
        long started, long completed, double positionSeconds, bool analysisEnabled,
        double positionUpdateMilliseconds, double analysisScheduleMilliseconds, long targetSecond)
    {
        if (!DesktopLog.PerformanceDiagnosticsEnabled) return;
        var gapMs = previousTick == 0 ? double.NaN : Stopwatch.GetElapsedTime(previousTick, started).TotalMilliseconds;
        var workMs = Stopwatch.GetElapsedTime(started, completed).TotalMilliseconds;
        previousTick = started;
        var sampleIndex = ticks % tickGap.Length;
        tickGap[sampleIndex] = gapMs;
        tickWork[sampleIndex] = workMs;
        positionUpdate[sampleIndex] = positionUpdateMilliseconds;
        analysisSchedule[sampleIndex] = analysisScheduleMilliseconds;
        ticks++;
        sampleCount = Math.Min(sampleCount + 1, tickGap.Length);
        if (double.IsFinite(gapMs))
        {
            if (gapMs > 33) gapsOver33Ms++;
            if (gapMs > 50) gapsOver50Ms++;
            maxGapMs = Math.Max(maxGapMs, gapMs);
        }
        maxWorkMs = Math.Max(maxWorkMs, workMs);
        if (double.IsFinite(gapMs) && gapMs >= 80)
            DesktopLog.Info($"tnk_playback_slow_tick scenario={DesktopLog.PerformanceScenario} recording_id={recordingId}" +
                $" playback_speed={speed.ToString("0.##", CultureInfo.InvariantCulture)}" +
                $" analysis_enabled={analysisEnabled.ToString().ToLowerInvariant()} target_second={targetSecond}" +
                $" position_s={positionSeconds.ToString("0.######", CultureInfo.InvariantCulture)}" +
                $" gap_ms={gapMs.ToString("0.0", CultureInfo.InvariantCulture)}" +
                $" tick_work_ms={workMs.ToString("0.0", CultureInfo.InvariantCulture)}" +
                $" position_update_ms={positionUpdateMilliseconds.ToString("0.000", CultureInfo.InvariantCulture)}" +
                $" analysis_schedule_ms={analysisScheduleMilliseconds.ToString("0.000", CultureInfo.InvariantCulture)}");
        if (reportStarted == 0)
        {
            reportStarted = started;
            processCpuAtReportStart = CaptureProcessCpuTime();
        }
        if (Stopwatch.GetElapsedTime(reportStarted, completed) < ReportInterval) return;

        var gap = Summarize(tickGap);
        var work = Summarize(tickWork);
        var position = Summarize(positionUpdate);
        var schedule = Summarize(analysisSchedule);
        var elapsedMs = Stopwatch.GetElapsedTime(reportStarted, completed).TotalMilliseconds;
        var processCpuMs = (CaptureProcessCpuTime() - processCpuAtReportStart).TotalMilliseconds;
        var processCpuPercent = elapsedMs > 0
            ? processCpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100
            : 0;
        DesktopLog.Info($"tnk_playback_tick_perf scenario={DesktopLog.PerformanceScenario} recording_id={recordingId}" +
            $" sampling_rate_hz={samplingRateHz.ToString("0.###", CultureInfo.InvariantCulture)}" +
            $" playback_speed={speed.ToString("0.##", CultureInfo.InvariantCulture)} analysis_enabled={analysisEnabled.ToString().ToLowerInvariant()}" +
            $" ticks={ticks} gap_over_33ms={gapsOver33Ms} gap_over_50ms={gapsOver50Ms}" +
            $" tick_gap_n={gap.Count} tick_gap_p50_ms={gap.P50:0.0} tick_gap_p95_ms={gap.P95:0.0} tick_gap_p99_ms={gap.P99:0.0}" +
            $" tick_work_n={work.Count} tick_work_p50_ms={work.P50:0.0} tick_work_p95_ms={work.P95:0.0} tick_work_p99_ms={work.P99:0.0}" +
            $" position_update_p95_ms={position.P95:0.000} analysis_schedule_p95_ms={schedule.P95:0.000}" +
            $" max_tick_gap_ms={maxGapMs:0.0} max_tick_work_ms={maxWorkMs:0.0}" +
            $" desktop_process_cpu_ms={processCpuMs:0.0} desktop_process_cpu_pct={processCpuPercent:0.0}" +
            $" logical_processors={Environment.ProcessorCount} position_s={positionSeconds:0.######}");
        reportStarted = completed;
        processCpuAtReportStart = CaptureProcessCpuTime();
        ticks = gapsOver33Ms = gapsOver50Ms = 0;
        maxGapMs = maxWorkMs = 0;
        Array.Clear(tickGap);
        Array.Clear(tickWork);
        Array.Clear(positionUpdate);
        Array.Clear(analysisSchedule);
        sampleCount = 0;
    }

    private TimingPercentiles Summarize(double[] samples)
    {
        var ordered = samples.Take(sampleCount).Where(double.IsFinite).Order().ToArray();
        return new(sampleCount, Percentile(ordered, .50), Percentile(ordered, .95), Percentile(ordered, .99));
    }

    private static double Percentile(double[] ordered, double percentile) => ordered.Length == 0 ? 0
        : ordered[Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1)];

    private static TimeSpan CaptureProcessCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }

    private readonly record struct TimingPercentiles(int Count, double P50, double P95, double P99);
}

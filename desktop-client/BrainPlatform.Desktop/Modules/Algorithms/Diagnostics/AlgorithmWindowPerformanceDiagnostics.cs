using System.Diagnostics;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

public enum AlgorithmWindowPerformanceSource
{
    FullRunMetricLocal,
    FullRunStructuredCache,
    FullRunIndexedRead,
    OnDemand,
    OnDemandFallback,
}

/// <summary>
/// Opt-in evidence for dynamic result source selection. It records identity and
/// timing metadata only; result arrays and request payloads are never logged.
/// </summary>
public static class AlgorithmWindowPerformanceDiagnostics
{
    public static bool Enabled => string.Equals(
        Environment.GetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS"),
        "1",
        StringComparison.Ordinal);

    public static void Record(
        DynamicWindowRequest request,
        string algorithmId,
        string? runId,
        AlgorithmWindowPerformanceSource source,
        string state,
        string? windowId,
        long elapsedMilliseconds)
    {
        if (!Enabled) return;

        DesktopLog.Info(
            $"algorithm_window_perf recording_id={request.RecordingId}" +
            $" algorithm_id={algorithmId}" +
            $" request_id={request.RequestId ?? "none"}" +
            $" run_id={runId ?? "none"}" +
            $" source={source}" +
            $" state={state}" +
            $" window_id={windowId ?? "none"}" +
            $" cursor_sample={request.CursorSample}" +
            $" elapsed_ms={elapsedMilliseconds}");
    }

    public static long ElapsedMilliseconds(long started) =>
        checked((long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds));
}

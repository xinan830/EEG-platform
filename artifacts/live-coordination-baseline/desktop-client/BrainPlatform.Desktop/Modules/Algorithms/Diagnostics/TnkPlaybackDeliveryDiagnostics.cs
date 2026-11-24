using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

/// <summary>Five-second bounded summaries for TNK event polling load and latency.</summary>
internal sealed class TnkPlaybackDeliveryDiagnostics
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);
    private readonly double[] requestDurationsMs = new double[256];
    private long intervalStarted;
    private int sampleCount;
    private int requests;
    private int events;
    private int batchesWithMore;
    private int incompleteBatches;

    internal void Record(long started, long completed, int eventCount, bool hasMore, bool incomplete)
    {
        if (!DesktopLog.PerformanceDiagnosticsEnabled) return;
        if (intervalStarted == 0) intervalStarted = started;
        var elapsed = Stopwatch.GetElapsedTime(started, completed).TotalMilliseconds;
        requestDurationsMs[sampleCount % requestDurationsMs.Length] = elapsed;
        sampleCount = Math.Min(sampleCount + 1, requestDurationsMs.Length);
        requests++;
        events += eventCount;
        if (hasMore) batchesWithMore++;
        if (incomplete) incompleteBatches++;
        if (Stopwatch.GetElapsedTime(intervalStarted, completed) < ReportInterval) return;

        var ordered = requestDurationsMs.Take(sampleCount).Order().ToArray();
        DesktopLog.Info($"tnk_playback_poll_perf scenario={DesktopLog.PerformanceScenario}" +
            $" requests={requests} events={events} batches_with_more={batchesWithMore}" +
            $" incomplete_batches={incompleteBatches} request_ms_p50={Percentile(ordered, .50):0.0}" +
            $" request_ms_p95={Percentile(ordered, .95):0.0} request_ms_p99={Percentile(ordered, .99):0.0}" +
            $" request_ms_max={(ordered.Length == 0 ? 0 : ordered[^1]).ToString("0.0", CultureInfo.InvariantCulture)}");
        intervalStarted = completed;
        sampleCount = requests = events = batchesWithMore = incompleteBatches = 0;
        Array.Clear(requestDurationsMs);
    }

    private static double Percentile(double[] ordered, double percentile) => ordered.Length == 0 ? 0
        : ordered[Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1)];
}

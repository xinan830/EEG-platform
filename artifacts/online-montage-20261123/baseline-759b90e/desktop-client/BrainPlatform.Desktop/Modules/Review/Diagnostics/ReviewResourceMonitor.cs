using System.Diagnostics;
using System.Globalization;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Review.Diagnostics;

public sealed record ReviewResourceContext(
    Guid ReviewSessionId, Guid RecordingSessionId, double PositionSeconds, bool IsPlaying);

public sealed record ReviewProcessResources(
    long WorkingSetBytes, long PrivateBytes, long ManagedBytes, long GcHeapBytes,
    int Gen0Collections, int Gen1Collections, int Gen2Collections)
{
    public static ReviewProcessResources Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new ReviewProcessResources(
            process.WorkingSet64, process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false), GC.GetGCMemoryInfo().HeapSizeBytes,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }
}

public sealed record ReviewResourceSample(
    ReviewResourceContext Context, ReviewProcessResources Process, double DispatcherQueueMilliseconds);

/// <summary>
/// Low-frequency, opt-in process and UI queue observations. At most one probe
/// can wait for the dispatcher; sampling never forces GC or reads signal data.
/// </summary>
public sealed class ReviewResourceMonitor : IDisposable
{
    private readonly Func<Action, bool> tryEnqueue;
    private readonly Func<ReviewResourceContext?> readContext;
    private readonly Func<ReviewProcessResources> capture;
    private readonly Action<ReviewResourceSample> record;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken stopToken;
    private readonly object timerGate = new();
    private System.Threading.Timer? timer;
    private int sampling;
    private int disposed;

    public ReviewResourceMonitor(
        Func<Action, bool> tryEnqueue,
        Func<ReviewResourceContext?> readContext,
        Action<ReviewResourceSample>? record = null,
        Func<ReviewProcessResources>? capture = null)
    {
        this.tryEnqueue = tryEnqueue;
        this.readContext = readContext;
        this.record = record ?? WriteSample;
        this.capture = capture ?? ReviewProcessResources.Capture;
        stopToken = lifetime.Token;
    }

    public void Start()
    {
        lock (timerGate)
        {
            if (Volatile.Read(ref disposed) != 0 || timer is not null) return;
            timer = new System.Threading.Timer(
                state => { _ = SampleAsync(); }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    public async Task SampleAsync()
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref sampling, 1, 0) != 0)
            return;

        try
        {
            var resources = capture();
            var completion = new TaskCompletionSource<(ReviewResourceContext?, double)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var queuedAt = Stopwatch.GetTimestamp();
            if (!tryEnqueue(() =>
                {
                    if (stopToken.IsCancellationRequested) return;
                    try
                    {
                        var delay = Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
                        completion.TrySetResult((readContext(), delay));
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                })) return;

            var (context, queueMilliseconds) = await completion.Task.WaitAsync(stopToken).ConfigureAwait(false);
            if (context is not null && !stopToken.IsCancellationRequested)
                record(new ReviewResourceSample(context, resources, queueMilliseconds));
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // A failed diagnostic probe must not interrupt review or escape a timer callback.
            DesktopLog.Warning($"review_resource_sample_failed exception_type={exception.GetType().Name}");
        }
        finally { Interlocked.Exchange(ref sampling, 0); }
    }

    public void Dispose()
    {
        lock (timerGate)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            timer?.Dispose();
            timer = null;
        }
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private static void WriteSample(ReviewResourceSample sample)
    {
        var context = sample.Context;
        var process = sample.Process;
        DesktopLog.Info(string.Create(CultureInfo.InvariantCulture,
            $"review_resource_perf review_session_id={context.ReviewSessionId:N}" +
            $" recording_id={context.RecordingSessionId:N} process_id={Environment.ProcessId}" +
            $" position_s={context.PositionSeconds:0.######} playing={context.IsPlaying}" +
            $" working_set_bytes={process.WorkingSetBytes} private_bytes={process.PrivateBytes}" +
            $" managed_bytes={process.ManagedBytes} gc_heap_bytes={process.GcHeapBytes}" +
            $" gen0={process.Gen0Collections} gen1={process.Gen1Collections} gen2={process.Gen2Collections}" +
            $" dispatcher_queue_ms={sample.DispatcherQueueMilliseconds:0.0}"));
    }
}

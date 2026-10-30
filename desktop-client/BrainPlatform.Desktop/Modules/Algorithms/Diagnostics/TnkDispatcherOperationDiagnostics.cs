using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;

namespace BrainPlatform.Desktop.Modules.Algorithms.Diagnostics;

internal sealed class TnkDispatcherOperationDiagnostics : IDisposable
{
    private const double SlowOperationThresholdMs = 40;
    private readonly Dispatcher dispatcher;
    private readonly ConcurrentDictionary<DispatcherOperation, long> postedOperations = new();
    private readonly ConcurrentDictionary<DispatcherOperation, (long Started, double QueueMs, uint NativeThreadId)> startedOperations = new();
    private bool disposed;

    internal TnkDispatcherOperationDiagnostics(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        dispatcher.Hooks.OperationPosted += OnOperationPosted;
        dispatcher.Hooks.OperationStarted += OnOperationStarted;
        dispatcher.Hooks.OperationCompleted += OnOperationCompleted;
        dispatcher.Hooks.OperationAborted += OnOperationAborted;
    }

    private void OnOperationPosted(object? sender, DispatcherHookEventArgs args)
    {
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            postedOperations[args.Operation] = Stopwatch.GetTimestamp();
    }

    private void OnOperationStarted(object? sender, DispatcherHookEventArgs args)
    {
        var now = Stopwatch.GetTimestamp();
        var queueMs = postedOperations.TryRemove(args.Operation, out var posted)
            ? Stopwatch.GetElapsedTime(posted, now).TotalMilliseconds : 0;
        if (DesktopLog.PerformanceDiagnosticsEnabled)
            startedOperations[args.Operation] = (now, queueMs, GetCurrentThreadId());
    }

    private void OnOperationCompleted(object? sender, DispatcherHookEventArgs args)
    {
        if (!startedOperations.TryRemove(args.Operation, out var timing)) return;
        var executionMs = Stopwatch.GetElapsedTime(timing.Started).TotalMilliseconds;
        if (executionMs < SlowOperationThresholdMs && timing.QueueMs < SlowOperationThresholdMs) return;

        var priority = args.Operation.Priority.ToString();
        ThreadPool.QueueUserWorkItem(_ => DesktopLog.Info(
            $"tnk_dispatcher_operation_slow native_thread_id={timing.NativeThreadId} priority={priority}" +
            $" queue_ms={timing.QueueMs:0.0} execution_ms={executionMs:0.0}"));
    }

    private void OnOperationAborted(object? sender, DispatcherHookEventArgs args)
    {
        postedOperations.TryRemove(args.Operation, out _);
        startedOperations.TryRemove(args.Operation, out _);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dispatcher.Hooks.OperationPosted -= OnOperationPosted;
        dispatcher.Hooks.OperationStarted -= OnOperationStarted;
        dispatcher.Hooks.OperationCompleted -= OnOperationCompleted;
        dispatcher.Hooks.OperationAborted -= OnOperationAborted;
        postedOperations.Clear();
        startedOperations.Clear();
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

using System.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>One owned observation and one replaceable target; never plans scientific windows.</summary>
public sealed class DynamicWindowTargetCoordinator
{
    internal const int MaximumSequentialPendingTargets = 16;
    private readonly object gate = new();
    private readonly Func<DynamicWindowRequest, CancellationToken, Task<DynamicWindowResponse>>? observe;
    private readonly IAlgorithmClient? client;
    private readonly TimeSpan cadence;
    private Work? active;
    private Work? pending;
    private readonly Queue<Work> sequentialPending = new();
    private (string Key, DynamicWindowResponse Response)? cached;
    private string? contextKey;
    private long generation;
    private TaskCompletionSource? drained;

    /// <summary>Raised for an owned result even when its cursor waiter has been superseded.</summary>
    public event Action<DynamicWindowRequest, DynamicWindowResponse>? WindowCompleted;

    public DynamicWindowTargetCoordinator(IAlgorithmClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        cadence = TimeSpan.FromMilliseconds(200);
    }

    public DynamicWindowTargetCoordinator(
        Func<DynamicWindowRequest, CancellationToken, Task<DynamicWindowResponse>> observe,
        TimeSpan? cadence = null)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.cadence = cadence ?? TimeSpan.FromMilliseconds(200);
        if (this.cadence < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cadence));
    }

    public Task<DynamicWindowResponse> LoadAsync(DynamicWindowRequest request, CancellationToken waiterToken,
        IProgress<DynamicWindowResponse>? progress = null)
    {
        waiterToken.ThrowIfCancellationRequested();
        Task<DynamicWindowResponse> result;
        lock (gate)
        {
            var context = ContextKey(request);
            if (contextKey != context)
            {
                ResetOwnedWork();
                contextKey = context;
            }
            // An explicit seek supersedes queued playback targets, including
            // the case where it rejoins the currently active target.
            DropSequentialPending();
            var key = $"{context}|{request.CursorSample}|{request.Retry}|{request.RetryOfWindowId}";
            if (cached is { } hit && hit.Key == key)
            {
                // Returning to the displayed target supersedes a queued seek too.
                DropPending();
                result = Task.FromResult(hit.Response);
            }
            else if (active is { } current && current.Key == key && current.Generation == generation)
            {
                DropPending();
                current.Progress = progress;
                result = current.Result.Task;
            }
            else if (pending is { } next && next.Key == key)
            {
                next.Progress = progress;
                result = next.Result.Task;
            }
            else
            {
                DropPending();
                pending = new Work(key, request, generation) { Progress = progress };
                result = pending.Result.Task;
                if (drained is null)
                {
                    drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = Task.Run(PumpAsync);
                }
            }
        }
        // Cursor movement cancels only the waiter. Owner reset cancels science.
        return result.WaitAsync(waiterToken);
    }

    /// <summary>Queues contiguous playback targets; direct seeks supersede this bounded queue.</summary>
    public Task<DynamicWindowResponse> LoadSequentialAsync(DynamicWindowRequest request, CancellationToken waiterToken)
    {
        waiterToken.ThrowIfCancellationRequested();
        Task<DynamicWindowResponse> result;
        lock (gate)
        {
            var context = ContextKey(request);
            if (contextKey != context)
            {
                ResetOwnedWork();
                contextKey = context;
            }
            var key = $"{context}|{request.CursorSample}|{request.Retry}|{request.RetryOfWindowId}";
            if (cached is { } hit && hit.Key == key)
                result = Task.FromResult(hit.Response);
            else if (active is { } current && current.Key == key && current.Generation == generation)
                result = current.Result.Task;
            else if (pending is { } next && next.Key == key)
                result = next.Result.Task;
            else if (sequentialPending.FirstOrDefault(work => work.Key == key) is { } queued)
                result = queued.Result.Task;
            else if (sequentialPending.Count >= MaximumSequentialPendingTargets)
                result = Task.FromException<DynamicWindowResponse>(new InvalidOperationException(
                    $"Playback analysis queue reached its {MaximumSequentialPendingTargets}-target limit."));
            else
            {
                var work = new Work(key, request, generation);
                sequentialPending.Enqueue(work);
                result = work.Result.Task;
                if (drained is null)
                {
                    drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = Task.Run(PumpAsync);
                }
            }
        }
        return result.WaitAsync(waiterToken);
    }

    public Task ResetAsync()
    {
        lock (gate)
        {
            ResetOwnedWork();
            contextKey = null;
            return active?.Finished.Task ?? Task.CompletedTask;
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            Work work;
            lock (gate)
            {
                if (pending is null && sequentialPending.Count == 0)
                {
                    drained!.TrySetResult();
                    drained = null;
                    return;
                }
                work = pending ?? sequentialPending.Dequeue();
                pending = null;
                active = work;
            }
            var started = Stopwatch.GetTimestamp();
            try
            {
                var response = client is null
                    ? await observe!(work.Request, work.Cancellation.Token).ConfigureAwait(false)
                    : await new DynamicWindowObserver(client).ObserveAsync(work.Request, work.Cancellation.Token,
                        response => ReportStatus(work, response)).ConfigureAwait(false);
                var publishCompleted = false;
                lock (gate)
                {
                    if (work.Generation != generation || work.Cancellation.IsCancellationRequested)
                        work.Result.TrySetCanceled();
                    else
                    {
                        if (response.State == "ready") cached = (work.Key, response);
                        publishCompleted = true;
                    }
                }
                if (publishCompleted) PublishCompleted(work.Request, response);
                work.Result.TrySetResult(response);
            }
            catch (OperationCanceledException) { work.Result.TrySetCanceled(); }
            catch (Exception error) { work.Result.TrySetException(error); }
            finally
            {
                try { await work.CancellationTask.ConfigureAwait(false); }
                catch (Exception error)
                {
                    BrainPlatform.Desktop.Infrastructure.Diagnostics.DesktopLog.Warning(
                        $"window_cancellation_callback_failed type={error.GetType().Name}");
                }
                lock (gate)
                {
                    active = null;
                    work.Cancellation.Dispose();
                    work.Finished.TrySetResult();
                }
            }
            // Bound start frequency without adding a fixed gap after a slow job.
            // Observation includes awaited subscription cleanup; ownership stays serial.
            var remaining = cadence - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining).ConfigureAwait(false);
        }
    }

    private void PublishCompleted(DynamicWindowRequest request, DynamicWindowResponse response)
    {
        var handlers = WindowCompleted;
        if (handlers is null) return;
        foreach (Action<DynamicWindowRequest, DynamicWindowResponse> handler in handlers.GetInvocationList())
        {
            try { handler(request, response); }
            catch (Exception error)
            {
                BrainPlatform.Desktop.Infrastructure.Diagnostics.DesktopLog.Warning(
                    $"window_completion_observer_failed type={error.GetType().Name}");
            }
        }
    }

    private void ResetOwnedWork()
    {
        generation++;
        cached = null;
        DropPending();
        DropSequentialPending();
        if (active is { } work)
        {
            work.Result.TrySetCanceled();
            work.CancellationTask = work.Cancellation.CancelAsync();
        }
    }

    private void ReportStatus(Work work, DynamicWindowResponse response)
    {
        IProgress<DynamicWindowResponse>? progress;
        lock (gate)
        {
            if (work.Generation != generation || pending is not null || work.Cancellation.IsCancellationRequested) return;
            progress = work.Progress;
        }
        progress?.Report(response);
    }

    private void DropPending()
    {
        if (pending is not { } work) return;
        pending = null;
        work.Result.TrySetCanceled();
        work.Cancellation.Dispose();
    }

    private void DropSequentialPending()
    {
        while (sequentialPending.Count > 0)
        {
            var work = sequentialPending.Dequeue();
            work.Result.TrySetCanceled();
            work.Cancellation.Dispose();
        }
    }

    private static string ContextKey(DynamicWindowRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            request.RecordingId, request.AlgorithmId, request.DefinitionId,
            request.DefinitionVersion, request.ProjectId, request.Config
        });

    private sealed class Work(string key, DynamicWindowRequest request, long generation)
    {
        public string Key { get; } = key;
        public DynamicWindowRequest Request { get; } = request;
        public long Generation { get; } = generation;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task CancellationTask { get; set; } = Task.CompletedTask;
        public IProgress<DynamicWindowResponse>? Progress { get; set; }
        public TaskCompletionSource<DynamicWindowResponse> Result { get; } =
            ObservedResult();
        public TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource<DynamicWindowResponse> ObservedResult()
        {
            var source = new TaskCompletionSource<DynamicWindowResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            // A superseded waiter may leave before transport fails. Still observe that fault.
            _ = source.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return source;
        }
    }
}

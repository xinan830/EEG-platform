using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>One owned observation and one replaceable target; never plans scientific windows.</summary>
public sealed class DynamicWindowTargetCoordinator
{
    private readonly object gate = new();
    private readonly Func<DynamicWindowRequest, CancellationToken, Task<DynamicWindowResponse>>? observe;
    private readonly IAlgorithmClient? client;
    private readonly TimeSpan cadence;
    private Work? active;
    private Work? pending;
    private (string Key, DynamicWindowResponse Response)? cached;
    private string? contextKey;
    private long generation;
    private TaskCompletionSource? drained;

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
                if (pending is null)
                {
                    drained!.TrySetResult();
                    drained = null;
                    return;
                }
                work = pending;
                pending = null;
                active = work;
            }
            try
            {
                var response = client is null
                    ? await observe!(work.Request, work.Cancellation.Token).ConfigureAwait(false)
                    : await new DynamicWindowObserver(client).ObserveAsync(work.Request, work.Cancellation.Token,
                        response => ReportStatus(work, response)).ConfigureAwait(false);
                lock (gate)
                {
                    if (work.Generation != generation || work.Cancellation.IsCancellationRequested)
                        work.Result.TrySetCanceled();
                    else
                    {
                        if (response.State == "ready") cached = (work.Key, response);
                        work.Result.TrySetResult(response);
                    }
                }
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
            // This is transport backpressure, not another playback/scientific clock.
            if (cadence > TimeSpan.Zero) await Task.Delay(cadence).ConfigureAwait(false);
        }
    }

    private void ResetOwnedWork()
    {
        generation++;
        cached = null;
        DropPending();
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

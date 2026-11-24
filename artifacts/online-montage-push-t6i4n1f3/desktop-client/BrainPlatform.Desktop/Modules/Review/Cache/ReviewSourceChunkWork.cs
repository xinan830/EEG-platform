namespace BrainPlatform.Desktop.Modules.Review.Cache;

/// <summary>One reusable chunk build with independently cancellable progress subscribers.</summary>
internal sealed class ReviewSourceChunkWork
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation;
    private readonly Dictionary<long, ProgressSubscriber> subscribers = [];
    private ReviewPreparationState? latestProgress;
    private long progressSequence;
    private long nextSubscriber;
    private int waiters;

    public ReviewSourceChunkWork(
        Func<CancellationToken, Action<ReviewPreparationState>, Task<ReviewFilteredSourceChunk>> build,
        CancellationToken lifetime)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Task = StartAsync(build);
        _ = Task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (gate) cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    public Task<ReviewFilteredSourceChunk> Task { get; }
    public bool IsReusable => !cancellation.IsCancellationRequested && !Task.IsCanceled && !Task.IsFaulted;

    public async Task<ReviewFilteredSourceChunk> WaitAsync(
        CancellationToken token, Action<ReviewPreparationState>? progress)
    {
        long subscriber = 0;
        try
        {
            ProgressSubscriber? observer = null;
            ReviewPreparationState? snapshot;
            long sequence;
            lock (gate)
            {
                waiters++;
                subscriber = ++nextSubscriber;
                if (progress is not null)
                {
                    observer = new ProgressSubscriber(token, progress);
                    subscribers[subscriber] = observer;
                }
                snapshot = latestProgress;
                sequence = progressSequence;
            }
            if (snapshot is not null) observer?.Report(snapshot, sequence);
            return await Task.WaitAsync(token);
        }
        finally
        {
            lock (gate)
            {
                subscribers.Remove(subscriber);
                if (--waiters == 0 && !Task.IsCompleted) _ = CancelWhenUnusedAsync();
            }
        }
    }

    private async Task<ReviewFilteredSourceChunk> StartAsync(
        Func<CancellationToken, Action<ReviewPreparationState>, Task<ReviewFilteredSourceChunk>> build) =>
        await build(cancellation.Token, Publish);

    private void Publish(ReviewPreparationState state)
    {
        ProgressSubscriber[] observers;
        long sequence;
        lock (gate)
        {
            if (cancellation.IsCancellationRequested) return;
            latestProgress = state;
            sequence = ++progressSequence;
            observers = subscribers.Values.ToArray();
        }
        // UI callbacks may close review; never invoke them under shared-work locks.
        foreach (var observer in observers) observer.Report(state, sequence);
    }

    private async Task CancelWhenUnusedAsync()
    {
        // Preserve the existing short grace period for a compatible drag target to join.
        await System.Threading.Tasks.Task.Delay(100);
        lock (gate)
        {
            if (waiters == 0 && !Task.IsCompleted) cancellation.Cancel();
        }
    }

    private sealed class ProgressSubscriber(CancellationToken token, Action<ReviewPreparationState> report)
    {
        private readonly object deliveryGate = new();
        private long lastSequence;

        public void Report(ReviewPreparationState state, long sequence)
        {
            lock (deliveryGate)
            {
                if (token.IsCancellationRequested || sequence <= lastSequence) return;
                lastSequence = sequence;
                report(state);
            }
        }
    }
}

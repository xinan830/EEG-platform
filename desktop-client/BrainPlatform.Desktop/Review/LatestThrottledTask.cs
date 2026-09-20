namespace BrainPlatform.Desktop.Review;

/// <summary>
/// Runs the newest requested action at a bounded cadence while input continues.
/// Unlike a debounce, continuous dragging still starts useful background work.
/// </summary>
internal sealed class LatestThrottledTask : IDisposable
{
    private readonly object gate = new();
    private readonly TimeSpan minimumInterval;
    private CancellationTokenSource? lifetime;
    private Func<CancellationToken, Task>? latest;
    private bool workerRunning;
    private int disposed;

    public LatestThrottledTask(TimeSpan minimumInterval)
    {
        if (minimumInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        this.minimumInterval = minimumInterval;
    }

    public void Schedule(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (gate)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            latest = action;
            if (workerRunning) return;

            workerRunning = true;
            lifetime = new CancellationTokenSource();
            _ = RunAsync(lifetime, lifetime.Token);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            latest = null;
            cancellation = lifetime;
            lifetime = null;
        }
        cancellation?.Cancel();
    }

    private async Task RunAsync(CancellationTokenSource owner, CancellationToken token)
    {
        try
        {
            while (true)
            {
                Func<CancellationToken, Task>? action;
                lock (gate)
                {
                    action = latest;
                    latest = null;
                }
                if (action is null) return;

                try
                {
                    await action(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Session-level loading reports its own failure state.
                }

                await Task.Delay(minimumInterval, token);
                lock (gate)
                {
                    if (latest is null) return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(lifetime, owner))
                {
                    lifetime = null;
                    workerRunning = false;
                }
            }
            owner.Dispose();
        }
    }
}

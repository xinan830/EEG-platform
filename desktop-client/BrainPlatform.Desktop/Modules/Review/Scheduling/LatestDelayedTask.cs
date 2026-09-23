namespace BrainPlatform.Desktop.Modules.Review.Scheduling;

/// <summary>Debounces work and guarantees that only the latest request may run.</summary>
internal sealed class LatestDelayedTask : IDisposable
{
    private CancellationTokenSource? pending;
    private int disposed;

    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var previous = Interlocked.Exchange(ref pending, new CancellationTokenSource());
        CancelAndDispose(previous);
        var current = pending!;
        _ = InvokeAsync(delay, action, current, current.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var current = Interlocked.Exchange(ref pending, null);
        CancelAndDispose(current);
    }

    private async Task InvokeAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action,
        CancellationTokenSource current,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref pending, null, current), current))
            {
                current.Dispose();
            }
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        source.Dispose();
    }
}

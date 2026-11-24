namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

/// <summary>Shared algorithm-only CPU admission; waveform display never waits on it.</summary>
public sealed class AlgorithmResourceScheduler
{
    public static AlgorithmResourceScheduler Shared { get; } = new(Environment.ProcessorCount > 4 ? 2 : 1);

    private readonly SemaphoreSlim slots;
    private readonly int maximumPendingJobs;
    private int pendingJobs;

    public AlgorithmResourceScheduler(int maximumConcurrentJobs, int maximumPendingJobs = 16)
    {
        if (maximumConcurrentJobs < 1) throw new ArgumentOutOfRangeException(nameof(maximumConcurrentJobs));
        if (maximumPendingJobs < maximumConcurrentJobs) throw new ArgumentOutOfRangeException(nameof(maximumPendingJobs));
        slots = new SemaphoreSlim(maximumConcurrentJobs, maximumConcurrentJobs);
        this.maximumPendingJobs = maximumPendingJobs;
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.Increment(ref pendingJobs) > maximumPendingJobs)
        {
            Interlocked.Decrement(ref pendingJobs);
            throw new InvalidOperationException("算法任务队列已达到资源上限。");
        }
        var acquired = false;
        try
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) slots.Release();
            Interlocked.Decrement(ref pendingJobs);
        }
    }
}

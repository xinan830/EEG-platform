namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Serial preparation of two future offline targets; never publishes a window.</summary>
internal sealed class ReviewPlaybackBuffer<T> : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<long, Entry> entries = [];
    private readonly Func<long, CancellationToken, Task<T>> prepare;
    private readonly CancellationTokenSource cancellation;
    private readonly CancellationToken token;
    private bool pumping;
    private bool stopped;

    public ReviewPlaybackBuffer(
        Func<long, CancellationToken, Task<T>> prepare, CancellationToken lifetime)
    {
        this.prepare = prepare;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        token = cancellation.Token;
    }

    public void Advance(long currentSample, long stepSamples, long durationSamples)
    {
        if (stepSamples <= 0) throw new ArgumentOutOfRangeException(nameof(stepSamples));
        lock (gate)
        {
            if (stopped || token.IsCancellationRequested) return;
            var aligned = currentSample / stepSamples * stepSamples;
            var desired = new[] { aligned + stepSamples, aligned + 2 * stepSamples }
                .Where(sample => sample <= durationSamples).ToHashSet();
            foreach (var sample in entries.Keys.Where(sample => !desired.Contains(sample)).ToArray())
            {
                var old = entries[sample];
                entries.Remove(sample);
                if (!old.Started) old.Completion.TrySetCanceled();
            }
            foreach (var sample in desired.Order()) entries.TryAdd(sample, new Entry());
            if (pumping || entries.Values.All(entry => entry.Started)) return;
            pumping = true;
            _ = PumpAsync();
        }
    }

    public Task<T>? Take(long sample)
    {
        lock (gate)
        {
            if (stopped || !entries.Remove(sample, out var entry)) return null;
            if (entry.Started) return entry.Completion.Task;
            entry.Completion.TrySetCanceled();
            return null;
        }
    }

    public void DiscardBefore(long sample)
    {
        lock (gate)
        {
            foreach (var old in entries.Keys.Where(target => target < sample).ToArray())
            {
                var entry = entries[old];
                entries.Remove(old);
                if (!entry.Started) entry.Completion.TrySetCanceled();
            }
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            KeyValuePair<long, Entry> next;
            lock (gate)
            {
                next = entries.OrderBy(pair => pair.Key).FirstOrDefault(pair => !pair.Value.Started);
                if (stopped || token.IsCancellationRequested || next.Value is null)
                {
                    pumping = false;
                    return;
                }
                next.Value.Started = true;
            }
            try
            {
                var result = await prepare(next.Key, token);
                token.ThrowIfCancellationRequested();
                next.Value.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { next.Value.Completion.TrySetCanceled(); }
            catch (Exception exception)
            {
                next.Value.Completion.TrySetException(exception);
                // A future target may be discarded before any foreground caller takes it.
                _ = next.Value.Completion.Task.Exception;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            cancellation.Cancel();
            foreach (var entry in entries.Values) entry.Completion.TrySetCanceled(token);
            entries.Clear();
            cancellation.Dispose();
        }
    }

    private sealed class Entry
    {
        public bool Started { get; set; }
        public TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

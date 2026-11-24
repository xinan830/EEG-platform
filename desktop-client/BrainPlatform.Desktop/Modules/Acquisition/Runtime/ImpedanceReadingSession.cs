namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

/// <summary>Owns background impedance polling and joins it before releasing the device.</summary>
internal sealed class ImpedanceReadingSession(IAcquisitionImpedanceStream stream, Guid sessionId)
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly object stopGate = new();
    private readonly SemaphoreSlim streamDisposeGate = new(1, 1);
    private Task? reader;
    private Task? stopTask;
    private bool streamReleased;

    public Guid SessionId { get; } = sessionId;
    public event EventHandler<ImpedanceSnapshot>? Updated;
    public event EventHandler<Exception>? Failed;

    public void Start() => reader = Task.Run(ReadAsync);

    private async Task ReadAsync()
    {
        try
        {
            await foreach (var snapshot in stream.ReadSnapshotsAsync(cancellation.Token).ConfigureAwait(false))
            {
                if (!cancellation.IsCancellationRequested && snapshot.DetectionSessionId == SessionId)
                    Updated?.Invoke(this, snapshot);
            }
            if (!cancellation.IsCancellationRequested)
                throw new AcquisitionUnavailableException("阻抗数据流已意外结束。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            try
            {
                await DisposeStreamOnceAsync().ConfigureAwait(false);
            }
            catch (Exception closeException)
            {
                exception = new AggregateException(exception, closeException);
            }
            Failed?.Invoke(this, exception);
        }
    }

    public Task StopAsync()
    {
        lock (stopGate)
        {
            if (stopTask is { IsCompleted: false } or { IsCompletedSuccessfully: true })
                return stopTask;

            return stopTask = StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }

        if (reader is not null) await reader.ConfigureAwait(false);
        await Task.Run(DisposeStreamOnceAsync).ConfigureAwait(false);
        cancellation.Dispose();
    }

    private async Task DisposeStreamOnceAsync()
    {
        await streamDisposeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (streamReleased) return;
            await stream.DisposeAsync().ConfigureAwait(false);
            streamReleased = true;
        }
        finally
        {
            streamDisposeGate.Release();
        }
    }
}

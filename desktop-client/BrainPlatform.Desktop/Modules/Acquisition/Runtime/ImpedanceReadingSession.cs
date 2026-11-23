namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

/// <summary>Owns background impedance polling and joins it before releasing the device.</summary>
internal sealed class ImpedanceReadingSession(IAcquisitionImpedanceStream stream, Guid sessionId)
{
    private readonly CancellationTokenSource cancellation = new();
    private Task? reader;
    private bool stopped;
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
                await stream.DisposeAsync().ConfigureAwait(false);
                streamReleased = true;
            }
            catch (Exception closeException)
            {
                exception = new AggregateException(exception, closeException);
            }
            Failed?.Invoke(this, exception);
        }
    }

    public async Task StopAsync()
    {
        if (stopped) return;
        cancellation.Cancel();
        if (reader is not null) await reader.ConfigureAwait(false);
        if (!streamReleased)
        {
            await Task.Run(async () => await stream.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            streamReleased = true;
        }
        stopped = true;
        cancellation.Dispose();
    }
}

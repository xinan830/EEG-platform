using System.Threading.Channels;

namespace BrainPlatform.Desktop.Modules.Acquisition.Storage;

/// <summary>
/// Keeps vendor reads independent from raw-file latency while preserving the
/// order of every persistence operation. The queue is deliberately bounded:
/// overload is a recording fault, never silent raw-data loss.
/// </summary>
public sealed class QueuedAcquisitionRawWriter : IAcquisitionRawWriter, IAcquisitionRawWriterHealth
{
    private const int DefaultCapacity = 256;
    private readonly IAcquisitionRawWriter inner;
    private readonly Channel<WriteOperation> operations;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task worker;
    private Exception? failure;
    private int terminalOperationQueued;
    private int innerDisposed;

    public QueuedAcquisitionRawWriter(IAcquisitionRawWriter inner, int capacity = DefaultCapacity)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        operations = Channel.CreateBounded<WriteOperation>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        worker = Task.Run(ConsumeAsync);
    }

    public string RecordingDirectory => inner.RecordingDirectory;

    public Exception? Failure => Volatile.Read(ref failure);

    public Task AppendBatchAsync(AcquisitionBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfFailed();
        Enqueue(new WriteOperation(
            (writer, token) => writer.AppendBatchAsync(batch, token),
            Completion: null));
        return Task.CompletedTask;
    }

    public Task AppendGapAsync(AcquisitionGap gap, CancellationToken cancellationToken) =>
        EnqueueAndWaitAsync((writer, token) => writer.AppendGapAsync(gap, token), cancellationToken);

    public Task AppendLifecycleBoundaryAsync(
        RecordingLifecycleBoundary boundary,
        CancellationToken cancellationToken) =>
        EnqueueAndWaitAsync((writer, token) => writer.AppendLifecycleBoundaryAsync(boundary, token), cancellationToken);

    public Task AppendDiagnosticAsync(
        string code,
        string detail,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken) =>
        EnqueueAndWaitAsync(
            (writer, token) => writer.AppendDiagnosticAsync(code, detail, occurredAtUtc, token),
            cancellationToken);

    public async Task CompleteAsync(DateTimeOffset completedAtUtc, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref terminalOperationQueued, 1) != 0)
        {
            ThrowIfFailed();
            return;
        }

        await EnqueueAndWaitAsync(
            (writer, token) => writer.CompleteAsync(completedAtUtc, token),
            cancellationToken);
        operations.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }

    public async Task AbortAsync(AcquisitionFault fault, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref terminalOperationQueued, 1) != 0)
        {
            await inner.AbortAsync(fault, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Failure is not null)
        {
            await worker.ConfigureAwait(false);
            await inner.AbortAsync(fault, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnqueueAndWaitAsync(
            (writer, token) => writer.AbortAsync(fault, token),
            cancellationToken);
        operations.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref terminalOperationQueued, 1) == 0 && Failure is null)
        {
            try
            {
                await EnqueueAndWaitAsync(
                    (writer, token) => writer.DisposeAsync().AsTask(),
                    CancellationToken.None);
                Volatile.Write(ref innerDisposed, 1);
            }
            catch
            {
                // The first persistence failure is retained by Failure.
            }
        }

        operations.Writer.TryComplete();
        shutdown.Cancel();
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            if (Interlocked.Exchange(ref innerDisposed, 1) == 0)
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            shutdown.Dispose();
        }
    }

    private Task EnqueueAndWaitAsync(
        Func<IAcquisitionRawWriter, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WriteOperation(execute, completion));
        return completion.Task.WaitAsync(cancellationToken);
    }

    private void Enqueue(WriteOperation operation)
    {
        if (!operations.Writer.TryWrite(operation))
        {
            var overflow = new InvalidOperationException(
                "The raw-write queue is full; recording cannot continue without risking silent sample loss.");
            Interlocked.CompareExchange(ref failure, overflow, null);
            operation.Completion?.TrySetException(overflow);
            // Let already accepted operations drain in order; the retained
            // failure prevents new work from being accepted.
            operations.Writer.TryComplete();
            throw overflow;
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var operation in operations.Reader.ReadAllAsync(shutdown.Token))
            {
                try
                {
                    await operation.Execute(inner, shutdown.Token).ConfigureAwait(false);
                    operation.Completion?.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref failure, exception, null);
                    operation.Completion?.TrySetException(exception);
                    operations.Writer.TryComplete(exception);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private void ThrowIfFailed()
    {
        if (Failure is { } exception)
        {
            throw new InvalidOperationException("Raw persistence has failed.", exception);
        }
    }

    private sealed record WriteOperation(
        Func<IAcquisitionRawWriter, CancellationToken, Task> Execute,
        TaskCompletionSource<object?>? Completion);
}

public interface IAcquisitionRawWriterHealth
{
    Exception? Failure { get; }
}

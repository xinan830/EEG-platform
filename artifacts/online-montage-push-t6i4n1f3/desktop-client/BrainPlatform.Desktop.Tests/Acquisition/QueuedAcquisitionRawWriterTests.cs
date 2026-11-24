using System.Diagnostics;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class QueuedAcquisitionRawWriterTests
{
    [Fact]
    public async Task BatchEnqueueDoesNotWaitForSlowWriter_ButCompleteDrainsInOrder()
    {
        var inner = new SlowRawWriter(TimeSpan.FromMilliseconds(100));
        await using var writer = new QueuedAcquisitionRawWriter(inner, capacity: 16);

        var started = Stopwatch.GetTimestamp();
        await writer.AppendBatchAsync(Batch(0), CancellationToken.None);
        await writer.AppendBatchAsync(Batch(2), CancellationToken.None);
        var enqueueMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        await writer.CompleteAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(enqueueMilliseconds < 80, $"enqueue took {enqueueMilliseconds:F1} ms");
        Assert.Equal([0L, 2L], inner.Batches);
        Assert.True(inner.Completed);
    }

    [Fact]
    public async Task FullQueueFailsInsteadOfDroppingBatch()
    {
        var inner = new SlowRawWriter(TimeSpan.FromMilliseconds(250));
        await using var writer = new QueuedAcquisitionRawWriter(inner, capacity: 1);

        await writer.AppendBatchAsync(Batch(0), CancellationToken.None);
        InvalidOperationException? exception = null;
        for (var firstSampleCounter = 2L; firstSampleCounter <= 12 && exception is null; firstSampleCounter += 2)
        {
            try
            {
                await writer.AppendBatchAsync(Batch(firstSampleCounter), CancellationToken.None);
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }
        }

        Assert.NotNull(exception);
        Assert.Contains("queue is full", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(writer.Failure);
    }

    private static AcquisitionBatch Batch(long firstSampleCounter) =>
        new(firstSampleCounter, 2, 1, [1e-6, 2e-6], DateTimeOffset.UtcNow);

    private sealed class SlowRawWriter(TimeSpan delay) : IAcquisitionRawWriter
    {
        public List<long> Batches { get; } = [];
        public bool Completed { get; private set; }
        public string RecordingDirectory => "test";

        public async Task AppendBatchAsync(AcquisitionBatch batch, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            Batches.Add(batch.FirstSampleCounter);
        }

        public Task AppendGapAsync(AcquisitionGap gap, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AppendLifecycleBoundaryAsync(
            RecordingLifecycleBoundary boundary,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AppendDiagnosticAsync(
            string code,
            string detail,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(DateTimeOffset completedAtUtc, CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public Task AbortAsync(AcquisitionFault fault, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

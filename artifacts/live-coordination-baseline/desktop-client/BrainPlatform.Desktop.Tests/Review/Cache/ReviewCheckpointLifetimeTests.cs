using System.Collections.Concurrent;
using PauseAt = BrainPlatform.Desktop.Tests.Review.CheckpointPauseAt;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewCheckpointLifetimeTests
{
    [Theory]
    [InlineData(PauseAt.Open, false)]
    [InlineData(PauseAt.Open, true)]
    [InlineData(PauseAt.Advance, false)]
    [InlineData(PauseAt.Advance, true)]
    [InlineData(PauseAt.Filter, false)]
    [InlineData(PauseAt.Filter, true)]
    [InlineData(PauseAt.Export, false)]
    [InlineData(PauseAt.Export, true)]
    public async Task Cancelled_operation_cannot_publish_late_success_or_failure(PauseAt stage, bool fails)
    {
        using var fixture = new ControlledCheckpointFixture(stage);
        using var cancellation = new CancellationTokenSource();
        var states = new ConcurrentQueue<ReviewPreparationState>();
        fixture.Coordinator.ProgressChanged += (_, state) => states.Enqueue(state);
        var operation = fixture.Start(cancellation.Token);
        await fixture.Backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var count = states.Count;
        if (fails) fixture.Backend.Release.TrySetException(new IOException("late fixture failure"));
        else fixture.Backend.Release.TrySetResult();
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(count, states.Count);
        Assert.DoesNotContain(states, state => state.Phase is ReviewPreparationPhase.Complete or ReviewPreparationPhase.Failed);
        Assert.NotNull(error);
        Assert.IsNotType<ObjectDisposedException>(error);
        Assert.Equal(fixture.Backend.OpenedSessions, fixture.Backend.DisposedSessions);
    }

    [Fact]
    public async Task Close_cancels_active_and_queued_work_without_waiting_for_a_noncooperative_step()
    {
        using var fixture = new ControlledCheckpointFixture(PauseAt.Advance);
        var active = fixture.Start();
        var token = await fixture.Backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = fixture.Start();
        var notifications = 0;
        fixture.Coordinator.ProgressChanged += (_, _) => Interlocked.Increment(ref notifications);

        await fixture.Coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(active.IsCompleted);
        Assert.True(token.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        fixture.Backend.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, notifications);
        Assert.Equal(1, fixture.Backend.DisposedSessions);
        await fixture.Coordinator.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Start());
    }
}

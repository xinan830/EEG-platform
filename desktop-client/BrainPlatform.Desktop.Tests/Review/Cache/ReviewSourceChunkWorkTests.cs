namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewSourceChunkWorkTests
{
    private static readonly ReviewPreparationState History = new(ReviewPreparationPhase.PreparingHistory, 1000, 1010, 1100);
    private static readonly ReviewPreparationState Filtering = new(ReviewPreparationPhase.FilteringTarget, 1000, 1100, 1110);
    private static readonly ReviewFilteredSourceChunk Result = new(new(0, 0, 1,
        [new(1000, 1, 1, [2e-6])]), "fixture");

    [Fact]
    public async Task Replaced_subscriber_detaches_while_another_subscriber_keeps_shared_work()
    {
        using var lifetime = new CancellationTokenSource();
        using var obsolete = new CancellationTokenSource();
        var completion = new TaskCompletionSource<ReviewFilteredSourceChunk>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ReviewPreparationState> publish = null!;
        var sourceToken = CancellationToken.None;
        var builds = 0;
        var work = new ReviewSourceChunkWork((token, report) =>
        {
            builds++;
            sourceToken = token;
            publish = report;
            report(History);
            return completion.Task;
        }, lifetime.Token);
        var oldStates = new List<ReviewPreparationState>();
        var currentStates = new List<ReviewPreparationState>();
        var old = work.WaitAsync(obsolete.Token, oldStates.Add);
        var current = work.WaitAsync(CancellationToken.None, currentStates.Add);
        Assert.Equal(History, Assert.Single(currentStates));
        obsolete.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        publish(Filtering);
        Assert.Equal(History, Assert.Single(oldStates));
        Assert.Equal(new[] { History, Filtering }, currentStates);
        Assert.False(sourceToken.IsCancellationRequested);
        Assert.Equal(1, builds);
        completion.TrySetResult(Result);
        Assert.Same(Result, await current);
    }

    [Fact]
    public async Task Prefetch_can_be_joined_by_a_foreground_waiter_without_restarting_work()
    {
        using var lifetime = new CancellationTokenSource();
        using var foreground = new CancellationTokenSource();
        var completion = new TaskCompletionSource<ReviewFilteredSourceChunk>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ReviewPreparationState> publish = null!;
        var work = new ReviewSourceChunkWork((_, report) => { publish = report; return completion.Task; }, lifetime.Token);
        var prefetch = work.WaitAsync(CancellationToken.None, null);
        publish(History);
        var states = new List<ReviewPreparationState>();
        var current = work.WaitAsync(foreground.Token, states.Add);
        Assert.Equal(History, Assert.Single(states));
        foreground.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current);
        publish(Filtering);
        Assert.Single(states);
        completion.TrySetResult(Result);
        Assert.Same(Result, await prefetch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_lifetime_cancellation_suppresses_late_progress(bool fails)
    {
        using var lifetime = new CancellationTokenSource();
        var completion = new TaskCompletionSource<ReviewFilteredSourceChunk>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ReviewPreparationState> publish = null!;
        var work = new ReviewSourceChunkWork((_, report) => { publish = report; return completion.Task; }, lifetime.Token);
        var states = new List<ReviewPreparationState>();
        var pending = work.WaitAsync(lifetime.Token, states.Add);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        publish(Filtering);
        Assert.Empty(states);
        if (fails) completion.TrySetException(new IOException("late source failure"));
        else completion.TrySetResult(Result);
        await Record.ExceptionAsync(() => work.Task);
        publish(History);
        Assert.Empty(states);
        Assert.False(work.IsReusable);
    }
}

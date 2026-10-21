using System.Diagnostics;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class DynamicWindowTargetCoordinatorTests
{
    [Fact]
    public async Task Slow_observation_does_not_add_a_second_cadence_before_pending_target()
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.FromMilliseconds(800));
        try
        {
            var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
            var active = await observer.NextAsync();
            var pending = coordinator.LoadAsync(Request(200), CancellationToken.None);
            await Task.Delay(900);
            active.Complete();
            await first;
            var next = await observer.NextAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
            next.Complete();
            await pending;
            Assert.Equal(1, observer.MaximumConcurrent);
        }
        finally { await coordinator.ResetAsync(); }
    }

    [Fact]
    public async Task Fast_observation_still_limits_the_frequency_of_target_starts()
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.FromMilliseconds(400));
        try
        {
            var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
            var active = await observer.NextAsync();
            var pending = coordinator.LoadAsync(Request(200), CancellationToken.None);
            active.Complete();
            await first;
            var next = await observer.NextAsync();
            next.Complete();
            await pending;
            Assert.True(Stopwatch.GetElapsedTime(active.Started, next.Started) >= TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, observer.MaximumConcurrent);
        }
        finally { await coordinator.ResetAsync(); }
    }

    [Fact]
    public async Task Rapid_targets_keep_one_active_and_only_the_latest_pending()
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
        var active = await observer.NextAsync();
        var obsolete = coordinator.LoadAsync(Request(200), CancellationToken.None);
        Task<DynamicWindowResponse>? latest = null;
        for (var sample = 201; sample <= 1000; sample++)
        {
            latest = coordinator.LoadAsync(Request(sample), CancellationToken.None);
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete);
        Assert.False(active.Token.IsCancellationRequested);
        Assert.Equal(1, observer.Count);
        active.Complete();
        await first;
        var last = await observer.NextAsync();
        Assert.Equal(1000, last.Request.CursorSample);
        last.Complete();
        await latest!;
        Assert.Equal(2, observer.Count);
        Assert.Equal(1, observer.MaximumConcurrent);
        await coordinator.ResetAsync();
    }

    [Fact]
    public async Task Cancelling_a_cursor_wait_does_not_cancel_owned_observation()
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        using var waiter = new CancellationTokenSource();
        var first = coordinator.LoadAsync(Request(100), waiter.Token);
        var active = await observer.NextAsync();
        waiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(active.Token.IsCancellationRequested);
        var join = coordinator.LoadAsync(Request(100), CancellationToken.None);
        active.Complete();
        var result = await join;
        Assert.Same(result, await coordinator.LoadAsync(Request(100), CancellationToken.None));
        Assert.Equal(1, observer.Count);
        await coordinator.ResetAsync();
    }

    [Fact]
    public async Task Returning_to_active_target_removes_pending_seek()
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
        var active = await observer.NextAsync();
        var pending = coordinator.LoadAsync(Request(200), CancellationToken.None);
        var join = coordinator.LoadAsync(Request(100), CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        active.Complete();
        Assert.Same(await first, await join);
        Assert.Equal(1, observer.Count);
        await coordinator.ResetAsync();
    }

    [Theory]
    [InlineData("recording")]
    [InlineData("config")]
    [InlineData("project")]
    [InlineData("definition")]
    public async Task Scientific_context_change_releases_old_subscription_and_rejects_late_result(string field)
    {
        var observer = new ControlledObserver(ignoreCancellation: true);
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
        var active = await observer.NextAsync();
        var changed = field switch
        {
            "recording" => Request(100) with { RecordingId = "other" },
            "project" => Request(100) with { ProjectId = "other" },
            "definition" => Request(100) with { DefinitionVersion = "v2" },
            _ => Request(100) with { Config = JsonSerializer.SerializeToElement(new { channel = "F7" }) }
        };
        var next = coordinator.LoadAsync(changed, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(active.Token.IsCancellationRequested);
        active.Complete();
        var replacement = await observer.NextAsync();
        Assert.Equal(changed, replacement.Request);
        replacement.Complete();
        await next;
        Assert.Equal(1, observer.MaximumConcurrent);
        await coordinator.ResetAsync();
    }

    [Fact]
    public async Task Close_waits_for_subscription_cleanup_and_reopen_does_not_reuse_old_cache()
    {
        var observer = new ControlledObserver(ignoreCancellation: true);
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
        var active = await observer.NextAsync();
        var release = coordinator.ResetAsync();
        Assert.True(active.Token.IsCancellationRequested);
        Assert.False(release.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        active.Complete();
        await release;
        var reopened = coordinator.LoadAsync(Request(100), CancellationToken.None);
        var replacement = await observer.NextAsync();
        replacement.Complete();
        await reopened;
        Assert.Equal(2, observer.Count);
        await coordinator.ResetAsync();
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task Execution_failure_is_not_cached(string state)
    {
        var observer = new ControlledObserver();
        var coordinator = new DynamicWindowTargetCoordinator(observer.Observe, TimeSpan.Zero);
        var first = coordinator.LoadAsync(Request(100), CancellationToken.None);
        (await observer.NextAsync()).Complete(state);
        Assert.Equal(state, (await first).State);
        var second = coordinator.LoadAsync(Request(100), CancellationToken.None);
        (await observer.NextAsync()).Complete();
        Assert.Equal("ready", (await second).State);
        await coordinator.ResetAsync();
    }

    private static DynamicWindowRequest Request(long sample) => new(
        "recording", "psd", null, null, JsonSerializer.SerializeToElement(new { channel = "F3" }),
        sample, RequestId: Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Unreachable_backend_fault_is_observable_and_next_target_can_retry()
    {
        var calls = 0;
        var coordinator = new DynamicWindowTargetCoordinator((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromException<DynamicWindowResponse>(new HttpRequestException("backend unavailable"));
        }, TimeSpan.Zero);
        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.LoadAsync(Request(100), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.LoadAsync(Request(100), CancellationToken.None));
        Assert.Equal(2, calls);
        await coordinator.ResetAsync();
    }

    private sealed class ControlledObserver(bool ignoreCancellation = false)
    {
        private readonly System.Threading.Channels.Channel<Call> calls =
            System.Threading.Channels.Channel.CreateUnbounded<Call>();
        private int concurrent;
        public int Count { get; private set; }
        public int MaximumConcurrent { get; private set; }

        public async Task<DynamicWindowResponse> Observe(DynamicWindowRequest request, CancellationToken token)
        {
            Count++;
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref concurrent));
            var call = new Call(request, token);
            await calls.Writer.WriteAsync(call);
            try
            {
                return ignoreCancellation ? await call.Result.Task : await call.Result.Task.WaitAsync(token);
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }

        public async Task<Call> NextAsync() =>
            await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Call(DynamicWindowRequest request, CancellationToken token)
    {
        public long Started { get; } = Stopwatch.GetTimestamp();
        public DynamicWindowRequest Request { get; } = request;
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<DynamicWindowResponse> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(string state = "ready") => Result.SetResult(new DynamicWindowResponse(
            "session", $"window-{Request.CursorSample}", Request.RequestId!, state,
            null, null, null, null, null, null));
    }
}

using BrainPlatform.Desktop.Modules.Algorithms.Api;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmResourceSchedulerTests
{
    [Fact]
    public async Task Limits_algorithm_jobs_without_serializing_waveform_callers()
    {
        var scheduler = new AlgorithmResourceScheduler(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.RunAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;
        var second = scheduler.RunAsync(_ => Task.FromResult(2), CancellationToken.None);
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task Cancellation_before_admission_does_not_run_algorithm()
    {
        var scheduler = new AlgorithmResourceScheduler(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.RunAsync(_ =>
        {
            called = true;
            return Task.FromResult(1);
        }, cancellation.Token));
        Assert.False(called);
    }

    [Fact]
    public async Task Rejects_work_above_the_global_pending_bound()
    {
        var scheduler = new AlgorithmResourceScheduler(1, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.RunAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scheduler.RunAsync(_ => Task.FromResult(2), CancellationToken.None));
        release.SetResult();
        Assert.Equal(1, await first);
    }
}

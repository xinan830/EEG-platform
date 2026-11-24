using BrainPlatform.Desktop.Modules.Review.Diagnostics;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewResourceMonitorTests
{
    private static readonly ReviewResourceContext Context = new(Guid.NewGuid(), Guid.NewGuid(), 43200, true);
    private static readonly ReviewProcessResources Resources = new(1000, 2000, 300, 400, 1, 2, 3);

    [Fact]
    public async Task Pending_dispatcher_probe_is_bounded_and_next_sample_can_run_after_completion()
    {
        var pending = new Queue<Action>();
        var samples = new List<ReviewResourceSample>();
        var captures = 0;
        using var monitor = new ReviewResourceMonitor(
            action => { pending.Enqueue(action); return true; }, () => Context,
            samples.Add, () => { captures++; return Resources; });

        var first = monitor.SampleAsync();
        await monitor.SampleAsync();
        Assert.False(first.IsCompleted);
        Assert.Single(pending);
        Assert.Equal(1, captures);
        pending.Dequeue()();
        await first;

        var sample = Assert.Single(samples);
        Assert.Equal(Context, sample.Context);
        Assert.Equal(Resources, sample.Process);
        Assert.True(sample.DispatcherQueueMilliseconds >= 0);

        var next = monitor.SampleAsync();
        pending.Dequeue()();
        await next;
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public async Task Unload_cancels_wait_without_reading_context_or_publishing_late_sample()
    {
        Action? pending = null;
        var reads = 0;
        var samples = new List<ReviewResourceSample>();
        var monitor = new ReviewResourceMonitor(
            action => { pending = action; return true; }, () => { reads++; return Context; },
            samples.Add, () => Resources);

        var task = monitor.SampleAsync();
        monitor.Dispose();
        await task;
        pending!();
        await monitor.SampleAsync();
        monitor.Dispose();

        Assert.Equal(0, reads);
        Assert.Empty(samples);
    }

    [Fact]
    public async Task Dispatcher_shutdown_does_not_leave_sampling_gate_locked()
    {
        var attempts = 0;
        var samples = new List<ReviewResourceSample>();
        using var monitor = new ReviewResourceMonitor(
            _ => { attempts++; return false; }, () => Context, samples.Add,
            () => Resources);

        await monitor.SampleAsync();
        await monitor.SampleAsync();
        Assert.Equal(2, attempts);
        Assert.Empty(samples);
    }

    [Fact]
    public async Task No_loaded_recording_produces_no_misattributed_resource_sample()
    {
        var samples = new List<ReviewResourceSample>();
        using var monitor = new ReviewResourceMonitor(
            action => { action(); return true; }, () => null,
            samples.Add, () => Resources);

        await monitor.SampleAsync();
        Assert.Empty(samples);
    }

    [Fact]
    public async Task Resource_read_failure_does_not_escape_or_block_later_samples()
    {
        var samples = new List<ReviewResourceSample>();
        var attempts = 0;
        using var monitor = new ReviewResourceMonitor(
            action => { action(); return true; }, () => Context, samples.Add,
            () => ++attempts == 1 ? throw new InvalidOperationException("diagnostic failure") : Resources);

        await monitor.SampleAsync();
        await monitor.SampleAsync();
        Assert.Single(samples);
    }

    [Fact]
    public async Task Context_failure_does_not_escape_or_leave_probe_waiting()
    {
        var samples = new List<ReviewResourceSample>();
        using var monitor = new ReviewResourceMonitor(
            action => { action(); return true; }, () => throw new InvalidOperationException("no context"),
            samples.Add, () => Resources);

        await monitor.SampleAsync();
        await monitor.SampleAsync();
        Assert.Empty(samples);
    }

    [Fact]
    public void Process_snapshot_reports_nonnegative_counters_without_forcing_collection()
    {
        var snapshot = ReviewProcessResources.Capture();
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.PrivateBytes > 0);
        Assert.True(snapshot.ManagedBytes >= 0);
        Assert.True(snapshot.GcHeapBytes >= 0);
        Assert.True(snapshot.Gen0Collections >= 0);
        Assert.True(snapshot.Gen1Collections >= 0);
        Assert.True(snapshot.Gen2Collections >= 0);
    }
}

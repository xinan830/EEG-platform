using System.Collections.Concurrent;
using System.Reflection;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewProgressTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Old_preparation_progress_cannot_override_a_cached_or_new_target(bool cached, bool fails)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var old = session.LoadViewportAsync(100, 105);
        var read = await reader.NextReadAsync();
        var progress = GetProgress(session, read.Token);
        progress(new(ReviewPreparationPhase.PreparingHistory, 1000, 1200, 3000));
        Assert.Equal(ReviewPreparationPhase.PreparingHistory, session.PreparationState.Phase);

        var current = session.LoadViewportAsync(cached ? 0 : 200, cached ? 5 : 205);
        var latest = cached ? null : await reader.NextReadAsync();
        var status = session.StatusText;
        var frame = session.CurrentFrame;
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        progress(new(fails ? ReviewPreparationPhase.Failed : ReviewPreparationPhase.FilteringTarget,
            1000, 3000, 3200, fails ? "obsolete progress failure" : null));
        Assert.Equal(status, session.StatusText);
        Assert.Same(frame, session.CurrentFrame);
        Assert.Equal(ReviewPreparationState.Idle, session.PreparationState);
        Assert.Equal(0, notifications);
        latest?.Complete();
        read.Complete();
        await Task.WhenAll(old, current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_dispatcher_progress_is_rechecked_after_completion_or_close(bool close)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var pending = session.LoadViewportAsync(100, 105);
        var read = await reader.NextReadAsync();
        var context = new QueuedContext();
        var original = SynchronizationContext.Current;
        Action<ReviewPreparationState> progress;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            progress = GetProgress(session, read.Token);
        }
        finally { SynchronizationContext.SetSynchronizationContext(original); }
        progress(new(ReviewPreparationPhase.PreparingHistory, 1000, 1200, 3000));
        Assert.Equal(1, context.Count);
        if (close) await session.DisposeAsync();
        read.Complete();
        await pending;
        var status = session.StatusText;
        var frame = session.CurrentFrame;
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        context.Drain();
        Assert.Equal(status, session.StatusText);
        Assert.Same(frame, session.CurrentFrame);
        Assert.Equal(0, notifications);
    }

    private static Action<ReviewPreparationState> GetProgress(RecordingReviewSession session, CancellationToken token)
    {
        var type = typeof(RecordingReviewSession);
        var revision = (long)type.GetField("revision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        return (Action<ReviewPreparationState>)type.GetMethod("CreatePreparationProgress",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [revision, token])!;
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queued = new();
        public int Count => queued.Count;
        public override void Post(SendOrPostCallback callback, object? state) => queued.Enqueue((callback, state));
        public void Drain() { while (queued.TryDequeue(out var item)) item.Callback(item.State); }
    }
}

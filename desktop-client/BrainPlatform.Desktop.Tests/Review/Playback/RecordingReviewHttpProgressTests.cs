using System.Net.Http;
using System.Reflection;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewHttpProgressTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_checkpoint_progress_stops_publishing_after_return_to_cached_frame(bool fails)
    {
        using var handler = new ControlledReviewHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture/") };
        var reader = new ControlledCheckpointFixture.Reader(250);
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10,
            new HttpRecordingReviewFilter(client));
        await session.InitializeAsync();
        var original = session.CurrentFrame;
        handler.HoldNextWarmup = true;
        var old = session.LoadViewportAsync(100, 105);
        var held = await handler.NextAsync();
        var work = ActiveSourceWork(session);
        Assert.Equal(ReviewPreparationPhase.PreparingHistory, session.PreparationState.Phase);
        Assert.True(session.IsLoading);
        await session.LoadViewportAsync(0, 5);
        var status = session.StatusText;
        var notifications = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref notifications);
        held.Complete(fails);
        var error = await Record.ExceptionAsync(() => work.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsNotType<TimeoutException>(error);
        await old;
        Assert.Equal(0, notifications);
        Assert.Same(original, session.CurrentFrame);
        Assert.Equal(status, session.StatusText);
        Assert.Equal(ReviewPreparationState.Idle, session.PreparationState);
        Assert.False(session.IsLoading);
        Assert.Equal(5, session.PositionSeconds);
    }

    [Fact]
    public async Task Http_prefetch_does_not_publish_foreground_preparation_status()
    {
        using var handler = new ControlledReviewHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture/") };
        var reader = new ControlledCheckpointFixture.Reader(250);
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10,
            new HttpRecordingReviewFilter(client));
        await session.InitializeAsync();
        var original = session.CurrentFrame;
        var status = session.StatusText;
        var state = session.PreparationState;
        var notifications = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref notifications);
        handler.HoldNextBatch = true;
        var prefetch = session.PrefetchNextAsync();
        var held = await handler.NextAsync();
        Assert.Equal(0, notifications);
        Assert.Equal(state, session.PreparationState);
        held.Complete();
        await prefetch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, notifications);
        Assert.Equal(state, session.PreparationState);
        Assert.Equal(status, session.StatusText);
        Assert.Same(original, session.CurrentFrame);
    }

    private static ReviewSourceChunkWork ActiveSourceWork(RecordingReviewSession session)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var loader = typeof(RecordingReviewSession).GetField("sourceLoader", flags)!.GetValue(session)!;
        var gate = typeof(ReviewSourceLoader).GetField("sourceChunkLoadGate", flags)!.GetValue(loader)!;
        var work = (Dictionary<string, ReviewSourceChunkWork>)typeof(ReviewSourceLoader)
            .GetField("sourceChunkLoads", flags)!.GetValue(loader)!;
        // Adjacent chunks may be prefetched in the background; the foreground
        // work is the first chunk requested for the visible viewport.
        lock (gate) return work.Values.First(item => !item.Task.IsCompleted);
    }
}

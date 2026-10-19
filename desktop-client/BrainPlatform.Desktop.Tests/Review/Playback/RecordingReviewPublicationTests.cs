using System.Collections.Concurrent;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewPublicationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Superseded_completion_cannot_publish_or_clear_latest_load(bool trackClick, bool fails)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var initial = session.CurrentFrame;
        var publications = new ConcurrentQueue<RecordingReviewFrame?>();
        session.Changed += (_, _) => publications.Enqueue(session.CurrentFrame);
        var obsolete = Navigate(session, 100, trackClick);
        var first = await reader.NextReadAsync();
        var latest = session.LoadViewportAsync(200, 205);
        var second = await reader.NextReadAsync();
        Assert.True(first.Token.IsCancellationRequested);

        if (fails) first.Fail(); else first.Complete();
        await obsolete.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(initial, session.CurrentFrame);
        Assert.True(session.IsLoading);
        Assert.Equal(205, session.PositionSeconds);
        Assert.Equal(200, session.ViewportStartSeconds);
        Assert.All(publications, frame => Assert.Same(initial, frame));

        second.Complete();
        await latest;
        Assert.False(session.IsLoading);
        AssertFrameSource(reader, session.CurrentFrame!, second.StartSeconds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Late_completion_cannot_replace_a_finished_new_target(bool trackClick, bool fails)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var obsolete = Navigate(session, 100, trackClick);
        var first = await reader.NextReadAsync();
        var latest = session.LoadViewportAsync(200, 205);
        var second = await reader.NextReadAsync();
        second.Complete();
        await latest;
        var published = session.CurrentFrame;
        var status = session.StatusText;
        var notifications = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref notifications);

        if (fails) first.Fail(); else first.Complete();
        await obsolete.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, notifications);
        Assert.Same(published, session.CurrentFrame);
        Assert.Equal(status, session.StatusText);
        Assert.Equal(205, session.PositionSeconds);
        AssertFrameSource(reader, session.CurrentFrame!, second.StartSeconds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposal_fences_pending_success_and_failure(bool trackClick, bool fails)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var initial = session.CurrentFrame;
        var pending = Navigate(session, 100, trackClick);
        var read = await reader.NextReadAsync();
        await session.DisposeAsync();
        var notifications = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref notifications);
        if (fails) read.Fail(); else read.Complete();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, notifications);
        Assert.Same(initial, session.CurrentFrame);
        Assert.False(session.IsLoading);
        Assert.True(read.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Cached_return_supersedes_pending_track_click_without_late_commit()
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var initial = session.CurrentFrame;
        var pending = session.NavigateFromTrackClickAsync(100, 105);
        var read = await reader.NextReadAsync();
        await session.LoadViewportAsync(0, 5);
        read.Complete();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(initial, session.CurrentFrame);
        Assert.Equal(5, session.PositionSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_failure_is_still_visible_and_keeps_the_complete_frame(bool trackClick)
    {
        using var reader = new ControlledReviewReader();
        await using var session = new RecordingReviewSession(reader, ControlledReviewReader.Catalog, 10);
        await session.InitializeAsync();
        var initial = session.CurrentFrame;
        var pending = Navigate(session, 100, trackClick);
        (await reader.NextReadAsync()).Fail();
        await pending;
        Assert.Same(initial, session.CurrentFrame);
        Assert.False(session.IsLoading);
        Assert.Contains("obsolete fixture read failed", session.StatusText);
    }

    private static async Task Navigate(RecordingReviewSession session, double start, bool trackClick)
    {
        if (trackClick) await session.NavigateFromTrackClickAsync(start, start + 5);
        else await session.LoadViewportAsync(start, start + 5);
    }

    private static void AssertFrameSource(ControlledReviewReader reader, RecordingReviewFrame frame, double start)
    {
        Assert.Equal(reader.Manifest.SessionId, frame.RecordingSessionId);
        Assert.Equal(start, frame.WindowStartSeconds);
        Assert.Equal(reader.FirstSampleCounter + (long)Math.Round(start * reader.Manifest.SamplingRateHz), frame.WindowStartSampleCounter);
        var channel = Assert.Single(Assert.Single(frame.Segments).Channels);
        Assert.Equal("F3", channel.Label);
        Assert.Equal((start + 1) * 1e-6, channel.Values[0]);
    }
}

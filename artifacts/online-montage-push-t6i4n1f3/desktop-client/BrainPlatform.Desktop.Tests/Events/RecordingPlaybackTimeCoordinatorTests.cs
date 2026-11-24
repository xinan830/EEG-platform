using BrainPlatform.Desktop.Shared.Time;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class RecordingPlaybackTimeCoordinatorTests
{
    [Fact]
    public void Uses_one_recording_relative_sample_cursor_for_all_events()
    {
        var clock = 10d;
        using var coordinator = new RecordingPlaybackTimeCoordinator("recording", 100, 1_000,
            () => clock);
        var events = new List<PlaybackCursorSnapshot>();
        coordinator.CursorChanged += events.Add;

        coordinator.Play();
        clock += 1.25;
        Assert.True(coordinator.Update());

        var snapshot = coordinator.Snapshot;
        Assert.Equal(125, snapshot.CursorSample);
        Assert.Equal(1.25, snapshot.CursorSeconds, 10);
        Assert.All(events, item => Assert.Equal(snapshot.RecordingId, item.RecordingId));
        Assert.All(events, item => Assert.Equal(snapshot.SamplingRateHz, item.SamplingRateHz));
    }

    [Fact]
    public void Seek_advances_generation_and_rejects_out_of_range_samples()
    {
        using var coordinator = new RecordingPlaybackTimeCoordinator("recording", 200, 2_000);
        var initialGeneration = coordinator.Snapshot.Generation;

        coordinator.SeekSeconds(2.5);

        Assert.Equal(500, coordinator.Snapshot.CursorSample);
        Assert.Equal(initialGeneration + 1, coordinator.Snapshot.Generation);
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.SeekSample(2_001));
    }

    [Fact]
    public void Completion_clamps_once_and_restart_starts_a_new_generation()
    {
        var clock = 0d;
        using var coordinator = new RecordingPlaybackTimeCoordinator("recording", 100, 100,
            () => clock);
        var events = new List<PlaybackCursorSnapshot>();
        coordinator.CursorChanged += events.Add;

        coordinator.Play();
        clock = 2;
        coordinator.Update();
        var completedGeneration = coordinator.Snapshot.Generation;
        Assert.True(coordinator.Snapshot.IsCompleted);
        Assert.Equal(100, coordinator.Snapshot.CursorSample);

        coordinator.Play();

        Assert.False(coordinator.Snapshot.IsCompleted);
        Assert.True(coordinator.Snapshot.IsPlaying);
        Assert.Equal(0, coordinator.Snapshot.CursorSample);
        Assert.Equal(completedGeneration + 1, coordinator.Snapshot.Generation);
        Assert.Contains(events, item => item.IsCompleted);
    }

    [Fact]
    public void Slow_subscriber_does_not_change_the_cursor_or_clock_owner()
    {
        var clock = 0d;
        using var coordinator = new RecordingPlaybackTimeCoordinator("recording", 100, 1_000,
            () => clock);
        var received = new List<long>();
        coordinator.CursorChanged += snapshot => received.Add(snapshot.CursorSample);

        coordinator.Play();
        clock = .5;
        coordinator.Update();
        clock = 1;
        coordinator.Update();

        Assert.Equal(1_000, coordinator.DurationSamples);
        Assert.Equal(100, coordinator.Snapshot.CursorSample);
        Assert.Equal(new long[] { 0, 50, 100 }, received);
    }
}

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewQueuedTargetTests
{
    [Fact]
    public async Task Drag_suppresses_intermediate_analysis_and_requests_final_cursor_on_release()
    {
        using var reader = new ControlledReviewReader();
        await using var vm = new RecordingReviewViewModel(reader, ControlledReviewReader.Catalog);
        await vm.InitializeAsync();
        var requestedSamples = new ConcurrentQueue<long>();
        var finalRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ReviewAnalysisCoordinator((runId, sample, _, _) =>
        {
            var requested = sample!.Value;
            requestedSamples.Enqueue(requested);
            finalRequested.TrySetResult();
            return Task.FromResult(new WindowPreviewResponse(runId, "recording", requested,
                requested / (double)reader.Manifest.SamplingRateHz, "available", [],
                new WindowPreviewWindow("window", 0, 0, 1, "Complete", "valid", []),
                false, null, null, new Dictionary<string, JsonElement>(),
                new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
                new Dictionary<string, JsonElement>(), "test", "test"));
        });
        var binding = new ReviewAnalysisBinding(coordinator, "recording", reader.Manifest.SamplingRateHz,
            checked((long)(reader.DurationSeconds * reader.Manifest.SamplingRateHz)));
        binding.AttachRun("run");
        typeof(RecordingReviewViewModel).GetField("analysisBinding", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, binding);

        vm.BeginTimelineDrag();
        vm.PreviewSeek(105);
        vm.PreviewSeek(205);
        Assert.Empty(requestedSamples);

        vm.EndTimelineDrag();
        await finalRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([checked((long)(205 * reader.Manifest.SamplingRateHz))], requestedSamples);
    }

    [Fact]
    public async Task Playback_ticks_reuse_a_pending_cold_block_without_cancelling_it()
    {
        using var reader = new ControlledReviewReader();
        var now = 0d;
        await using var vm = new RecordingReviewViewModel(reader, ControlledReviewReader.Catalog,
            playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        var initial = vm.CurrentFrame;
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.CurrentFrame) && !ReferenceEquals(initial, vm.CurrentFrame))
                published.TrySetResult();
        };

        vm.TogglePlayback();
        now = 105;
        vm.TickPlayback();
        var pending = await reader.NextReadAsync();
        for (var tick = 1; tick <= 100; tick++)
        {
            now = 105 + tick * 0.01;
            vm.TickPlayback();
        }

        Assert.Equal(106, vm.PositionSeconds, precision: 6);
        Assert.True(vm.IsPlaying);
        Assert.True(vm.IsLoading);
        Assert.False(pending.Token.IsCancellationRequested);
        Assert.Equal(1, reader.DeferredReadCount);
        Assert.Same(initial, vm.CurrentFrame);
        Assert.False(published.Task.IsCompleted);

        pending.Complete();
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(pending.StartSeconds, vm.CurrentFrame!.WindowStartSeconds);
        Assert.Equal(reader.Manifest.SessionId, vm.CurrentFrame.RecordingSessionId);
        Assert.Equal(106, vm.PositionSeconds, precision: 6);
        Assert.False(vm.IsLoading);
        Assert.Equal(1, reader.DeferredReadCount);
    }

    [Fact]
    public async Task New_drag_target_fences_old_frame_before_throttled_load_starts()
    {
        using var reader = new ControlledReviewReader();
        await using var vm = new RecordingReviewViewModel(reader, ControlledReviewReader.Catalog);
        await vm.InitializeAsync();
        var initial = vm.CurrentFrame;
        var published = new ConcurrentQueue<RecordingReviewFrame>();
        var finalFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(vm.CurrentFrame) || vm.CurrentFrame is not { } frame) return;
            published.Enqueue(frame);
            if (frame.WindowStartSeconds > 150) finalFrame.TrySetResult();
        };

        vm.BeginTimelineDrag();
        vm.PreviewSeek(105);
        var old = await reader.NextReadAsync();
        vm.PreviewSeek(205);
        Assert.Equal(205, vm.PositionSeconds);
        // During a cold drag target the axis stays on the last complete frame;
        // it is committed atomically with the replacement frame.
        Assert.Equal(0, vm.ViewportStartSeconds);
        Assert.Same(initial, vm.CurrentFrame);
        old.Complete();
        var latest = await reader.NextReadAsync();

        Assert.Same(initial, vm.CurrentFrame);
        Assert.Empty(published);
        Assert.True(old.Token.IsCancellationRequested);
        latest.Complete();
        await finalFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(published);
        Assert.Equal(latest.StartSeconds, vm.CurrentFrame!.WindowStartSeconds);
        Assert.Equal(reader.Manifest.SessionId, vm.CurrentFrame.RecordingSessionId);
        Assert.Equal(205, vm.PositionSeconds);
        Assert.Equal(200, vm.ViewportStartSeconds);
        vm.EndTimelineDrag();
    }
}

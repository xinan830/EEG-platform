using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewAnalysisBindingTests
{
    [Fact]
    public async Task Attached_run_loads_window_at_recording_cursor()
    {
        var coordinator = new ReviewAnalysisCoordinator((runId, sample, _, _) =>
            Task.FromResult(Response(runId, "recording", sample!.Value)));
        await using var binding = new ReviewAnalysisBinding(coordinator, "recording", 100, 1_000);
        binding.AttachRun("run");

        await binding.LoadAtSecondsAsync(2.5);

        Assert.Equal("run", binding.RunId);
        Assert.Equal(250, binding.CurrentWindow!.CursorSample);
        Assert.Equal("分析 窗口已更新。", binding.StatusText);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Failure_is_presented_as_status_and_does_not_throw_from_binding()
    {
        var coordinator = new ReviewAnalysisCoordinator((_, _, _, _) =>
            throw new InvalidOperationException("窗口索引不可用"));
        await using var binding = new ReviewAnalysisBinding(coordinator, "recording", 100, 1_000);
        binding.AttachRun("run");

        await binding.LoadAtSampleAsync(100);

        Assert.Null(binding.CurrentWindow);
        Assert.Contains("窗口索引不可用", binding.StatusText);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Keeps_target_cursor_separate_from_the_backend_window_cursor()
    {
        var coordinator = new ReviewAnalysisCoordinator((runId, sample, _, _) =>
            Task.FromResult(Response(runId, "recording", sample!.Value - 100)));
        await using var binding = new ReviewAnalysisBinding(coordinator, "recording", 100, 1_000);
        binding.AttachRun("run");

        await binding.LoadAtSampleAsync(500);

        Assert.Equal(500, binding.TargetCursorSample);
        Assert.Equal(400, binding.DisplayedCursorSample);
        Assert.False(binding.IsTargetDisplayed);
    }

    [Fact]
    public async Task Playback_refreshes_at_one_second_cursor_intervals_and_release_uses_exact_sample()
    {
        var requested = new System.Collections.Concurrent.ConcurrentQueue<long>();
        await using var binding = new ReviewAnalysisBinding(
            new ReviewAnalysisCoordinator((runId, sample, _, _) =>
            {
                requested.Enqueue(sample!.Value);
                return Task.FromResult(Response(runId, "recording", sample.Value));
            }), "recording", 100, 10_000);
        binding.AttachRun("run");

        binding.OnPlaybackCursorAdvanced(1.2);
        binding.OnPlaybackCursorAdvanced(1.9);
        binding.OnPlaybackCursorAdvanced(2.1);
        await WaitUntilAsync(() => requested.Count >= 2);
        binding.OnCursorDragCompleted(2.9);
        await WaitUntilAsync(() => requested.Contains(290));

        Assert.Contains(100, requested);
        Assert.Contains(200, requested);
        Assert.Contains(290, requested);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(predicate(), "Expected analysis target was not submitted.");
    }

    private static WindowPreviewResponse Response(string runId, string recordingId, long sample) => new(
        runId, recordingId, sample, sample / 100d, "available", [],
        new WindowPreviewWindow("window", 0, 0, 100, "Complete", "valid", []),
        false, null, null, new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>(), "test", "test");

}

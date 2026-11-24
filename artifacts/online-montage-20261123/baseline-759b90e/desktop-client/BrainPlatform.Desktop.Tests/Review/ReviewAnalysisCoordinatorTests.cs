using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewAnalysisCoordinatorTests
{
    [Fact]
    public async Task Older_window_response_cannot_replace_newer_cursor()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ReviewAnalysisCoordinator(async (runId, sample, _, _) =>
        {
            if (sample == 100)
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
            return Response(sample!.Value);
        });

        var first = coordinator.LoadAtSampleAsync("recording", "run", 100, CancellationToken.None);
        await firstStarted.Task;
        var second = await coordinator.LoadAtSampleAsync("recording", "run", 200, CancellationToken.None);
        releaseFirst.SetResult();
        var stale = await first;

        Assert.NotNull(second);
        Assert.Null(stale);
        Assert.Equal(200, coordinator.Context!.CursorSample);
    }

    [Fact]
    public async Task Reset_clears_context_and_invalidates_pending_response()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ReviewAnalysisCoordinator(async (_, _, _, _) =>
        {
            await release.Task;
            return Response(100);
        });
        var pending = coordinator.LoadAtSampleAsync("recording", "run", 100, CancellationToken.None);
        coordinator.Reset();
        release.SetResult();

        Assert.Null(await pending);
        Assert.Null(coordinator.Context);
        Assert.Null(coordinator.CurrentWindow);
    }

    private static WindowPreviewResponse Response(long sample) => new(
        "run", "recording", sample, sample / 100d, "available", [],
        new WindowPreviewWindow("window", 0, 0, 100, "Complete", "valid", []),
        false, null, null, new Dictionary<string, System.Text.Json.JsonElement>(),
        new Dictionary<string, System.Text.Json.JsonElement>(),
        new Dictionary<string, System.Text.Json.JsonElement>(),
        new Dictionary<string, System.Text.Json.JsonElement>(), "test", "test");
}

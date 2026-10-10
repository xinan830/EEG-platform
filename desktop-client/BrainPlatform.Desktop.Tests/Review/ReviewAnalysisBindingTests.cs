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
        Assert.Equal("PSD 窗口已更新。", binding.StatusText);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Provider_rejects_a_window_from_another_recording()
    {
        var client = new FakeAlgorithmClient(Response("run", "other-recording", 10));
        var provider = new PsdReviewAnalysisProvider(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.LoadAsync("recording", "run", 10, CancellationToken.None));

        Assert.Contains("不属于当前回溯记录", exception.Message);
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

    private static WindowPreviewResponse Response(string runId, string recordingId, long sample) => new(
        runId, recordingId, sample, sample / 100d, "available", [],
        new WindowPreviewWindow("window", 0, 0, 100, "Complete", "valid", []),
        false, null, null, new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
        new Dictionary<string, JsonElement>(), "test", "test");

    private sealed class FakeAlgorithmClient(WindowPreviewResponse response) : IAlgorithmClient
    {
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WindowPreviewResponse> GetWindowPreviewAsync(
            string runId, long? cursorSample, double? cursorSeconds, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}

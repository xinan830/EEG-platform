using System.Collections.Concurrent;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewOnDemandProgressRaceTests
{
    [Fact]
    public async Task Late_progress_and_result_from_old_target_cannot_replace_new_target()
    {
        var provider = new ControlledProvider();
        var dispatcher = new QueuedContext();
        await using var binding = CreateBinding(provider);
        binding.AttachOnDemand(Request());
        var old = dispatcher.Start(() => binding.LoadAtSampleAsync(100));
        provider.Calls[0].Report("preparing_source");
        var current = dispatcher.Start(() => binding.LoadAtSampleAsync(500));
        provider.Calls[1].Report("computing_window");
        dispatcher.Drain();
        Assert.Contains("正在计算当前", binding.StatusText);

        provider.Calls[0].Report("preparing_source");
        provider.Calls[0].Complete();
        await dispatcher.FinishAsync(old);
        Assert.Contains("正在计算当前", binding.StatusText);
        Assert.Equal(500, binding.TargetCursorSample);
        Assert.Null(binding.CurrentWindow);
        Assert.True(binding.IsLoading);

        provider.Calls[1].Complete();
        await dispatcher.FinishAsync(current);
        Assert.Equal(500, binding.DisplayedCursorSample);
        Assert.Equal("PSD 窗口已更新。", binding.StatusText);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Detach_and_reopen_rejects_progress_from_previous_configuration()
    {
        var provider = new ControlledProvider();
        var dispatcher = new QueuedContext();
        await using var binding = CreateBinding(provider);
        binding.AttachOnDemand(Request());
        var old = dispatcher.Start(() => binding.LoadAtSampleAsync(100));
        provider.Calls[0].Report("preparing_source");
        binding.DetachRun();
        dispatcher.Drain();
        Assert.Equal("未绑定分析结果。", binding.StatusText);
        Assert.False(binding.IsLoading);

        binding.AttachOnDemand(Request("F7"));
        var reopened = dispatcher.Start(() => binding.LoadAtSampleAsync(600));
        provider.Calls[1].Report("queued");
        dispatcher.Drain();
        var expected = binding.StatusText;
        provider.Calls[0].Report("computing_window");
        provider.Calls[0].Complete();
        await dispatcher.FinishAsync(old);
        Assert.Equal(expected, binding.StatusText);
        Assert.Equal(600, binding.TargetCursorSample);
        Assert.Null(binding.CurrentWindow);

        provider.Calls[1].Complete();
        await dispatcher.FinishAsync(reopened);
        Assert.Equal(600, binding.DisplayedCursorSample);
        Assert.Equal("F7", provider.Calls[1].Request.Config.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Progress_delivered_after_completion_cannot_overwrite_terminal_status()
    {
        var provider = new ControlledProvider();
        var dispatcher = new QueuedContext();
        await using var binding = CreateBinding(provider);
        binding.AttachOnDemand(Request());
        var load = dispatcher.Start(() => binding.LoadAtSampleAsync(500));
        provider.Calls[0].Complete();
        await dispatcher.FinishAsync(load);
        provider.Calls[0].Report("preparing_source");
        dispatcher.Drain();
        Assert.Equal("PSD 窗口已更新。", binding.StatusText);
        Assert.Equal(500, binding.DisplayedCursorSample);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Automatic_preview_coalesces_ticks_inside_one_dynamic_step()
    {
        var provider = new ControlledProvider();
        await using var binding = CreateBinding(provider);
        binding.AttachOnDemand(RequestWithStep());

        binding.PreviewAtSeconds(4.1);
        binding.PreviewAtSeconds(4.2);
        binding.PreviewAtSeconds(4.9);

        Assert.Single(provider.Calls);
        Assert.Equal(400, binding.TargetCursorSample);
        Assert.True(binding.IsLoading);
    }

    private static ReviewAnalysisBinding CreateBinding(ControlledProvider provider) =>
        new(new ReviewAnalysisCoordinator(provider), "recording", 100, 1_000);

    private static AnalysisRunRequest Request(string channel = "F3") => new(
        "recording", "official_algorithm",
        JsonSerializer.SerializeToElement(new { algorithm_id = "psd", channel }));

    private static AnalysisRunRequest RequestWithStep() => new(
        "recording", "official_algorithm",
        JsonSerializer.SerializeToElement(new { algorithm_id = "psd", channel = "F3", refresh_step_s = 1.0 }));

    // Queue Progress<T> delivery like the WPF dispatcher, without relying on sleeps
    // to choose the ordering of old and current callbacks.
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();

        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));

        public Task Start(Func<Task> action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { return action(); }
            finally { SetSynchronizationContext(previous); }
        }

        public void Drain()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (callbacks.TryDequeue(out var work)) work.Callback(work.State);
            }
            finally { SetSynchronizationContext(previous); }
        }

        public async Task FinishAsync(Task task)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < timeout)
            {
                Drain();
                await Task.Delay(10);
            }
            Drain();
            await task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    private sealed class ControlledProvider : IReviewAnalysisProvider
    {
        public string AlgorithmId => "psd";
        public bool SupportsOnDemandWindows => true;
        public List<Call> Calls { get; } = [];

        public Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
            long cursorSample, CancellationToken cancellationToken, IProgress<DynamicWindowResponse>? progress)
        {
            // Deliberately ignore cancellation to model a late transport response.
            var call = new Call(request, cursorSample, progress);
            Calls.Add(call);
            return call.Result.Task;
        }

        public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
            Action<AnalysisRunResponse>? onStatusChanged, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId,
            long cursorSample, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Call(AnalysisRunRequest request, long sample, IProgress<DynamicWindowResponse>? progress)
    {
        public AnalysisRunRequest Request { get; } = request;
        public TaskCompletionSource<WindowPreviewResponse> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Report(string state) => progress?.Report(new DynamicWindowResponse(
            "session", $"window-{sample}", "request", state, null, null, null, null, null, null));

        public void Complete() => Result.SetResult(new WindowPreviewResponse(
            "window-run", "recording", sample, sample / 100d, "available", [],
            new WindowPreviewWindow($"window-{sample}", 0, 0, sample, "Complete", "valid", []),
            false, null, null, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "test", "test"));
    }
}

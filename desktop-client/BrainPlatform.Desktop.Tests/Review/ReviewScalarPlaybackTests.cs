using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewScalarPlaybackTests
{
    [Theory]
    [InlineData("faa")]
    [InlineData("iapf")]
    public async Task Prepares_two_future_windows_and_publishes_only_at_the_configured_step(string algorithm)
    {
        var provider = new ScalarPlaybackProvider(algorithm);
        await using var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true, 7);
        binding.OnPlaybackCursorAdvanced(20.2, 7);
        Assert.Equal(new long[] { 10000, 10500 }, provider.Samples);
        provider.Calls[1].Complete();
        await Until(() => provider.Calls.Count == 3);
        provider.Calls[2].Complete();
        await Until(() => provider.Calls[2].Finished);
        Assert.Equal(10000, binding.DisplayedCursorSample);

        binding.OnPlaybackCursorAdvanced(21, 7);
        await Until(() => binding.DisplayedCursorSample == 10500);
        Assert.Single(provider.Samples, sample => sample == 10500);
        Assert.Equal(10500, binding.TargetCursorSample);
        Assert.False(binding.IsLoading);

        provider.Automatic = true;
        await Until(() => provider.Calls.Count == 4);
        provider.Calls[3].Complete();
        for (var second = 22; second <= 26; second++)
        {
            await Until(() => provider.Samples.Contains((second + 1) * 500));
            binding.OnPlaybackCursorAdvanced(second, 7);
            await Until(() => binding.DisplayedCursorSample == second * 500);
            Assert.Single(provider.Samples, sample => sample == second * 500);
            Assert.All(provider.Samples, sample => Assert.True(sample <= (second + 2) * 500));
        }
    }

    [Theory]
    [InlineData("faa")]
    [InlineData("iapf")]
    public async Task Pause_cancels_future_work_and_late_transport_cannot_publish(string algorithm)
    {
        var provider = new ScalarPlaybackProvider(algorithm);
        await using var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true);
        var future = provider.Calls[1];
        binding.SetPlaybackActive(false);
        Assert.True(future.Token.IsCancellationRequested);
        future.Complete();
        await Until(() => future.Finished);
        Assert.Equal(10000, binding.DisplayedCursorSample);
        Assert.Equal(2, provider.Calls.Count);

        binding.SetPlaybackActive(true);
        Assert.Equal(10500, provider.Calls[2].Sample);
        binding.OnPlaybackCursorAdvanced(21);
        binding.SetPlaybackActive(false);
        provider.Calls[2].Complete();
        await Until(() => provider.Calls[2].Finished);
        Assert.Equal(10000, binding.DisplayedCursorSample);
        Assert.False(binding.IsLoading);
    }

    [Theory]
    [InlineData("faa")]
    [InlineData("iapf")]
    public async Task Overload_retains_only_the_latest_target_without_cancelling_active_science(string algorithm)
    {
        var provider = new ScalarPlaybackProvider(algorithm);
        await using var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true);
        binding.OnPlaybackCursorAdvanced(21);
        binding.OnPlaybackCursorAdvanced(22);
        binding.OnPlaybackCursorAdvanced(24);
        Assert.Equal(2, provider.Calls.Count);
        Assert.False(provider.Calls[1].Token.IsCancellationRequested);
        provider.Calls[1].Complete();
        await Until(() => provider.Calls.Count == 3);
        Assert.Equal(new long[] { 10000, 10500, 12000 }, provider.Samples);
        Assert.Equal(10000, binding.DisplayedCursorSample);
        Assert.Equal(12000, binding.TargetCursorSample);
        provider.Calls[2].Complete();
        await Until(() => binding.DisplayedCursorSample == 12000);
    }

    [Theory]
    [InlineData("seek")]
    [InlineData("drag")]
    [InlineData("configuration")]
    [InlineData("detach")]
    public async Task New_lifecycle_fences_prepared_windows_that_ignore_cancellation(string action)
    {
        var provider = new ScalarPlaybackProvider("faa");
        await using var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true);
        var future = provider.Calls[1];
        Task? load = null;
        if (action == "seek") binding.OnCursorPositionSelected(40, 1);
        else if (action == "drag") binding.OnCursorDragCompleted(40, 1);
        else if (action == "configuration")
        {
            binding.AttachOnDemand(Request("faa", "F7"));
            load = binding.LoadAtSampleAsync(20000);
        }
        else binding.DetachRun();
        Assert.True(future.Token.IsCancellationRequested);
        if (action != "detach")
        {
            provider.Calls[2].Complete();
            if (load is not null) await load;
            await Until(() => binding.DisplayedCursorSample == 20000);
        }
        future.Complete();
        await Until(() => future.Finished);
        if (action == "detach") Assert.Null(binding.CurrentWindow);
        else Assert.Equal(20000, binding.DisplayedCursorSample);
    }

    [Fact]
    public async Task Failed_preparation_is_reported_at_its_target_without_early_clearing()
    {
        var provider = new ScalarPlaybackProvider("iapf");
        await using var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true);
        provider.Calls[1].Result.SetException(new InvalidOperationException("source failed"));
        await Until(() => provider.Calls[1].Finished);
        Assert.Equal(10000, binding.DisplayedCursorSample);
        binding.OnPlaybackCursorAdvanced(21);
        await Until(() => binding.CurrentWindow is null);
        Assert.Contains("source failed", binding.StatusText);
        Assert.False(binding.IsLoading);
    }

    [Fact]
    public async Task Disposal_cancels_active_preparation_and_stateful_tnk_does_not_opt_in()
    {
        var provider = new ScalarPlaybackProvider("faa");
        var binding = CreateBinding(provider);
        await InitialWindow(binding, provider);
        binding.SetPlaybackActive(true);
        await binding.DisposeAsync();
        Assert.True(provider.Calls[1].Token.IsCancellationRequested);
        provider.Calls[1].Complete();
        await Until(() => provider.Calls[1].Finished);
        Assert.Null(binding.CurrentWindow);

        var tnk = new ScalarPlaybackProvider("tnk");
        await using var stateful = CreateBinding(tnk);
        await InitialWindow(stateful, tnk);
        stateful.SetPlaybackActive(true);
        Assert.Single(tnk.Calls);
    }

    private static ReviewAnalysisBinding CreateBinding(ScalarPlaybackProvider provider)
    {
        var binding = new ReviewAnalysisBinding(new ReviewAnalysisCoordinator(provider), "registered", 500, 50000,
            new RegisteredRecording("registered", "fixture", ".wpf", "now", 500, 100, ChannelNames, "sha"));
        binding.AttachOnDemand(Request(provider.AlgorithmId));
        return binding;
    }

    private static AnalysisRunRequest Request(string algorithm, string channel = "F3") => new("registered",
        "official_algorithm", Json(new { algorithm_id = algorithm, mode = "dynamic", channel,
            dynamic_window_s = 20, refresh_step_s = 1 }));

    private static async Task InitialWindow(ReviewAnalysisBinding binding, ScalarPlaybackProvider provider)
    {
        var initial = binding.LoadAtSampleAsync(10000);
        provider.Calls[0].Complete();
        await initial;
    }

    internal static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}

internal sealed class ScalarPlaybackProvider(string algorithm) : IReviewAlgorithmAdapter
{
    public string AlgorithmId => algorithm;
    public bool SupportsOnDemandWindows => true;
    public List<ScalarPlaybackCall> Calls { get; } = [];
    public long[] Samples => Calls.Select(call => call.Sample).ToArray();
    public bool Automatic { get; set; }

    public async Task<WindowPreviewResponse> LoadOnDemandAsync(string recordingId, AnalysisRunRequest request,
        long sample, CancellationToken token, IProgress<DynamicWindowResponse>? progress)
    {
        var call = new ScalarPlaybackCall(algorithm, request, sample, token);
        Calls.Add(call);
        if (Automatic) call.Complete();
        try { return await call.Result.Task; } // Deliberately model a transport that ignores cancellation.
        finally { call.Finished = true; }
    }

    public Task<RegisteredRecording> RegisterAsync(string directory, CancellationToken token,
        IReadOnlyList<MontageProfile>? montageProfiles = null) => Task.FromResult(
            new RegisteredRecording("registered", "fixture", ".wpf", "now", 500, 100, ChannelNames, "sha"));
    public Task<AlgorithmCatalogItem> GetAlgorithmAsync(CancellationToken token) => Task.FromResult(
        new AlgorithmCatalogItem("official", algorithm, "fixture-v1", algorithm, "", "", [], ["dynamic"], default,
            new DynamicAnalysisPolicy(4, [20], 20, 1, true), "available", true, "definition", "1", "impl"));
    public Task<AnalysisRunResponse> EnsureRunAsync(AnalysisRunRequest request,
        Action<AnalysisRunResponse>? status, CancellationToken token) => throw new NotSupportedException();
    public Task<WindowPreviewResponse> LoadAsync(string recordingId, string runId,
        long sample, CancellationToken token) => throw new NotSupportedException();
}

internal sealed class ScalarPlaybackCall(string algorithm, AnalysisRunRequest request, long sample, CancellationToken token)
{
    public long Sample => sample;
    public CancellationToken Token => token;
    public bool Finished { get; set; }
    public TaskCompletionSource<WindowPreviewResponse> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Complete() => Result.SetResult(Window(algorithm, sample, request.Config));
}

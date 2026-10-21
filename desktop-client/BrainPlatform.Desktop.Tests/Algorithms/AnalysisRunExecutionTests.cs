using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AnalysisRunExecutionTests
{
    [Fact]
    public async Task CatalogBounds_PreventSubmissionAndReleaseBusyState()
    {
        var client = new ExecutionClient();
        var catalog = await ReadyCatalog(client);
        catalog.SelectedAlgorithm = AlgorithmParameterValidationTests.Catalog();
        catalog.HighFrequencyText = "40";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(catalog, "RunAsync"));
        Assert.Contains("频段上限不能大于 30 Hz", error.Message);
        Assert.Equal(0, client.Created);
        Assert.False(catalog.IsRunActive);
    }

    [Theory]
    [InlineData("queued", 0, 7, 0)]
    [InlineData("running", 2, 7, 28.5714285714)]
    [InlineData("running", 7, 7, 99)]
    [InlineData("completed", 7, 7, 100)]
    [InlineData("cancelled", 2, 7, 28.5714285714)]
    public void SharedProgress_UsesMeasuredWindowsAndWaitsForPersistence(string status, int done, int total, double percent)
    {
        var catalog = new AlgorithmListViewModel(new ExecutionClient(), new OperationNotificationCenter());
        var context = new AnalysisContextViewModel(catalog);
        var notifications = new List<string?>();
        context.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        SetLastRun(catalog, Response(status, done, total));

        Assert.Equal(percent, context.RunProgressPercent, precision: 8);
        Assert.Equal(status is "queued" or "running", context.CanCancelRun);
        Assert.Equal(status is "queued" or "running", context.IsRunning);
        Assert.Contains(nameof(AnalysisContextViewModel.RunProgressPercent), notifications);
        Assert.Contains(nameof(AnalysisContextViewModel.CanCancelRun), notifications);
        if (status == "running") Assert.Contains($"{done}/{total}", context.RunProgressText);
    }

    [Fact]
    public async Task CancelHttp_PostsToRunEndpointAndDeserializesPartialProgress()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/runs/run-1/cancel", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Response("cancelled", 2, 7)) };
        })) { BaseAddress = new Uri("http://localhost/") };
        var response = await new HttpAlgorithmClient(http).CancelRunAsync("run-1", CancellationToken.None);
        Assert.Equal("cancelled", response.Status);
        Assert.Equal(2, response.ProgressCompleted);
        Assert.Equal(7, response.ProgressTotal);
    }

    [Fact]
    public async Task ConcurrentAutomaticAndButtonAttempts_CreateOnlyOneRunAndCanRerunAfterCancellation()
    {
        var client = new ExecutionClient();
        var catalog = await ReadyCatalog(client);
        var first = Invoke(catalog, "RunAsync");
        await Invoke(catalog, "RunAsync");
        Assert.Equal(1, client.Created);
        Assert.True(catalog.IsRunActive);
        await Invoke(catalog, "CancelRunAsync");
        await first;
        Assert.False(catalog.IsRunActive);
        Assert.Equal("cancelled", catalog.LastRun!.Status);
        client.Cancelled = false;
        var second = Invoke(catalog, "RunAsync");
        Assert.Equal(2, client.Created);
        await Invoke(catalog, "CancelRunAsync");
        await second;
        Assert.All(client.Requests, request => Assert.Null(request.IdempotencyKey));
    }

    [Fact]
    public async Task ParametersChangedWhileRunning_DoNotApplyOldCompletedPreview()
    {
        var client = new ExecutionClient { FinalStatus = "completed" };
        var catalog = await ReadyCatalog(client);
        var run = Invoke(catalog, "RunAsync");
        catalog.LowFrequencyText = "5";
        await run;
        Assert.Equal("cancelled", catalog.LastRun!.Status);
        Assert.False(catalog.IsRunActive);
        Assert.Equal("暂无结果摘要。", catalog.ResultSummaryText);
        Assert.Equal("1", client.Requests[0].Config.GetProperty("low_hz").GetDouble().ToString());
    }

    [Fact]
    public async Task Stale_poll_response_cannot_replace_cancelled_run_after_configuration_change()
    {
        var client = new ExecutionClient { PendingPoll = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var catalog = await ReadyCatalog(client);
        var run = Invoke(catalog, "RunAsync");
        await client.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        catalog.LowFrequencyText = "5";
        await client.CancellationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.PendingPoll.SetResult(Response("completed", 7, 7));
        await run;

        Assert.Equal("cancelled", catalog.LastRun!.Status);
        Assert.Equal("暂无结果摘要。", catalog.ResultSummaryText);
    }

    [Fact]
    public async Task ObsoleteRunAlreadyCompleted_RefreshesStatusWithoutCancellationError()
    {
        var notifications = new OperationNotificationCenter();
        var client = new ExecutionClient
        {
            CancelError = new(HttpStatusCode.Conflict, "RUN_NOT_CANCELLABLE", "run in completed state cannot be cancelled"),
            LatestRun = Response("completed", 7, 7),
        };
        var catalog = new AlgorithmListViewModel(client, notifications);
        SetLastRun(catalog, Response("running", 2, 7));

        await Invoke(catalog, "CancelObsoleteRunAsync", catalog.LastRun!);

        Assert.Equal("completed", catalog.LastRun!.Status);
        Assert.Contains("已结束：completed", catalog.RunStatusText);
        Assert.False(notifications.IsVisible);
        Assert.Equal(1, client.CancelAttempts);
        Assert.Equal(1, client.RefreshAttempts);
    }

    [Fact]
    public async Task ConcurrentObsoleteRunInvalidations_SendOnlyOneCancelRequest()
    {
        var client = new ExecutionClient { PendingCancel = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter());
        var run = Response("running", 2, 7);
        SetLastRun(catalog, run);

        var first = Invoke(catalog, "CancelObsoleteRunAsync", run);
        var second = Invoke(catalog, "CancelObsoleteRunAsync", run);
        await client.CancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, client.CancelAttempts);
        client.PendingCancel.SetResult(Response("cancelled", 2, 7));
        await Task.WhenAll(first, second);

        Assert.Equal("cancelled", catalog.LastRun!.Status);
    }

    private static async Task<AlgorithmListViewModel> ReadyCatalog(ExecutionClient client)
    {
        var catalog = new AlgorithmListViewModel(client, new OperationNotificationCenter())
        {
            SelectedAlgorithm = new AlgorithmCatalogItem("official", "peak_frequency", "official-peak-frequency-v1",
                "峰频率", "Peak", "", [], ["static", "dynamic"], default,
                new DynamicAnalysisPolicy(4, [5, 10], 5, 1, true), "available", true, null, null, null),
        };
        var recording = await client.RegisterWpfRecordingAsync("D:\\fixture", CancellationToken.None);
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.RegisteredRecording))!
            .SetValue(catalog, recording);
        foreach (var channel in recording.Channels) catalog.RegisteredChannels.Add(channel);
        catalog.SelectedChannel = "F3";
        catalog.EndSecondsText = "30";
        catalog.LowFrequencyText = "1";
        catalog.HighFrequencyText = "30";
        return catalog;
    }

    private static Task Invoke(AlgorithmListViewModel catalog, string name, params object[] args) =>
        (Task)typeof(AlgorithmListViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(catalog, args)!;

    private static void SetLastRun(AlgorithmListViewModel catalog, AnalysisRunResponse run) =>
        typeof(AlgorithmListViewModel).GetProperty(nameof(AlgorithmListViewModel.LastRun))!.SetValue(catalog, run);

    private static AnalysisRunResponse Response(string status, int done = 0, int total = 7) => new(
        "run-1", "recording-1", "official_algorithm", status, "v1", null, null, null, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, done, total);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class ExecutionClient : IAlgorithmClient, IRecordingRegistrationClient
    {
        public int Created { get; private set; }
        public bool Cancelled { get; set; }
        public string FinalStatus { get; init; } = "running";
        public AlgorithmApiException? CancelError { get; init; }
        public AnalysisRunResponse? LatestRun { get; init; }
        public TaskCompletionSource<AnalysisRunResponse>? PendingCancel { get; init; }
        public TaskCompletionSource CancelStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CancelAttempts { get; private set; }
        public int RefreshAttempts { get; private set; }
        public List<AnalysisRunRequest> Requests { get; } = [];
        public TaskCompletionSource<AnalysisRunResponse>? PendingPoll { get; init; }
        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RegisteredRecording> RegisterWpfRecordingAsync(string sourceDirectory, CancellationToken cancellationToken,
            IReadOnlyList<BrainPlatform.Desktop.Modules.Montages.Domain.MontageProfile>? analysisMontageProfiles = null) =>
            Task.FromResult(new RegisteredRecording("recording-1", "fixture", ".wpf", "2026-09-27T00:00:00Z", 250, 30, ["F3", "F4"], "hash"));
        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken)
        {
            Created++;
            Requests.Add(request);
            return Task.FromResult(Response("queued"));
        }
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            RefreshAttempts++;
            if (LatestRun is not null) return Task.FromResult(LatestRun);
            if (PendingPoll is null)
                return Task.FromResult(Response(Cancelled ? "cancelled" : FinalStatus));
            PollStarted.TrySetResult();
            return PendingPoll.Task;
        }
        public Task<AnalysisRunResponse> CancelRunAsync(string runId, CancellationToken cancellationToken)
        {
            CancelAttempts++;
            CancelStarted.TrySetResult();
            if (CancelError is not null) throw CancelError;
            if (PendingCancel is not null) return PendingCancel.Task;
            Cancelled = true;
            CancellationCompleted.TrySetResult();
            return Task.FromResult(Response("cancelled"));
        }
        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

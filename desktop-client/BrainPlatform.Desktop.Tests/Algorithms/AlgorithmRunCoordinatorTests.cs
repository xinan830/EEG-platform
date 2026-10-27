using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class AlgorithmRunCoordinatorTests
{
    [Fact]
    public async Task RegisterRecording_ForwardsCompletedRecordingDirectory()
    {
        var client = new CoordinatorClient();
        var coordinator = new AlgorithmRunCoordinator(client);

        var recording = await coordinator.RegisterRecordingAsync("D:\\recordings\\completed", CancellationToken.None);

        Assert.Equal("D:\\recordings\\completed", client.RegisteredDirectory);
        Assert.Equal("recording-1", recording.Id);
    }

    [Fact]
    public async Task CreateRunAndWait_PollsQueuedRunUntilTerminalState()
    {
        var client = new CoordinatorClient();
        var coordinator = new AlgorithmRunCoordinator(client);
        var observedStatuses = new List<string>();
        using var config = JsonDocument.Parse("{}");

        var result = await coordinator.CreateRunAndWaitAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", config.RootElement),
            run => observedStatuses.Add(run.Status),
            CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, client.PollCount);
        Assert.Equal(["queued", "completed"], observedStatuses);
    }

    [Fact]
    public async Task SupersededRunCreation_CancelsReturnedIdentityWithoutPublishingIt()
    {
        var client = new CoordinatorClient { PendingCreate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var coordinator = new AlgorithmRunCoordinator(client);
        using var config = JsonDocument.Parse("{}");
        var published = new List<string>();
        var task = coordinator.CreateRunAndWaitAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", config.RootElement),
            run => published.Add(run.RunId), CancellationToken.None, generation: 1);

        coordinator.SupersedeForeground(generation: 2);
        client.PendingCreate.SetResult(new AnalysisRunResponse(
            "run-1", "recording-1", "official_algorithm", "queued", "official-psd-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await client.CancelCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["run-1"], client.CancelledRunIds);
        Assert.Empty(published);
    }

    [Fact]
    public async Task SupersededPolling_ReleasesForegroundOwnerImmediately()
    {
        var client = new CoordinatorClient { PendingPoll = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var coordinator = new AlgorithmRunCoordinator(client);
        using var config = JsonDocument.Parse("{}");
        var task = coordinator.CreateRunAndWaitAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", config.RootElement),
            _ => { }, CancellationToken.None, generation: 1);
        await client.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.SupersedeForeground(generation: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await client.CancelCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["run-1"], client.CancelledRunIds);
    }

    [Fact]
    public async Task LatestForegroundRunStartsWhileSupersededPollingIsUnresolved()
    {
        var client = new CoordinatorClient { PendingPoll = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var coordinator = new AlgorithmRunCoordinator(client);
        using var config = JsonDocument.Parse("{}");
        var obsolete = coordinator.CreateRunAndWaitAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", config.RootElement),
            _ => { }, CancellationToken.None, generation: 1);
        await client.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.SupersedeForeground(generation: 2);
        var latest = await coordinator.CreateRunAndWaitAsync(
            new AnalysisRunRequest("recording-1", "official_algorithm", config.RootElement),
            _ => { }, CancellationToken.None, generation: 2);

        Assert.Equal("completed", latest.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete);
        Assert.Equal(["run-1"], client.CancelledRunIds);
    }

    private sealed class CoordinatorClient : IAlgorithmClient, IRecordingRegistrationClient
    {
        public string? RegisteredDirectory { get; private set; }
        public int PollCount { get; private set; }
        public TaskCompletionSource<AnalysisRunResponse>? PendingCreate { get; init; }
        public TaskCompletionSource<AnalysisRunResponse>? PendingPoll { get; init; }
        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> CancelledRunIds { get; } = [];

        public Task<RegisteredRecording> RegisterWpfRecordingAsync(string sourceDirectory, CancellationToken cancellationToken,
            IReadOnlyList<BrainPlatform.Desktop.Modules.Montages.Domain.MontageProfile>? analysisMontageProfiles = null)
        {
            RegisteredDirectory = sourceDirectory;
            return Task.FromResult(new RegisteredRecording(
                "recording-1", "completed", ".wpf", "2026-09-27T00:00:00Z", 2000, 10, ["O1"], "hash"));
        }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            PendingCreate?.Task ?? Task.FromResult(CreateRun("queued"));

        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            PollCount++;
            if (PendingPoll is not null && PollCount == 1)
            {
                PollStarted.TrySetResult();
                return PendingPoll.Task.WaitAsync(cancellationToken);
            }
            return Task.FromResult(CreateRun("completed"));
        }

        public Task<AnalysisRunResponse> CancelRunAsync(string runId, CancellationToken cancellationToken)
        {
            CancelledRunIds.Add(runId);
            CancelCompleted.TrySetResult();
            return Task.FromResult(CreateRun("cancelled"));
        }

        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static AnalysisRunResponse CreateRun(string status) => new(
            "run-1", "recording-1", "official_algorithm", status, "official-psd-v1", null, null,
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}

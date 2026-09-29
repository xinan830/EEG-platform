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

    private sealed class CoordinatorClient : IAlgorithmClient, IRecordingRegistrationClient
    {
        public string? RegisteredDirectory { get; private set; }
        public int PollCount { get; private set; }

        public Task<RegisteredRecording> RegisterWpfRecordingAsync(string sourceDirectory, CancellationToken cancellationToken)
        {
            RegisteredDirectory = sourceDirectory;
            return Task.FromResult(new RegisteredRecording(
                "recording-1", "completed", ".wpf", "2026-09-27T00:00:00Z", 2000, 10, ["O1"], "hash"));
        }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateRun("queued"));

        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            PollCount++;
            return Task.FromResult(CreateRun("completed"));
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

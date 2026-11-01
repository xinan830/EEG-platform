using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewTnkAnalysisAdapterTests
{
    [Fact]
    public async Task Review_requests_ordered_delivery_and_backward_seek_uses_a_new_delivery_id()
    {
        var client = new FixtureClient();
        var adapter = new ReviewTnkAnalysisAdapter(client);
        var recording = await adapter.RegisterAsync("fixture", CancellationToken.None);
        var algorithm = await adapter.GetAlgorithmAsync(CancellationToken.None);
        var request = new AnalysisRunRequest(recording.Id, "official_algorithm", Config());
        var snapshot = ReviewAnalysisSnapshot.From(request, recording, 0, algorithm);

        await adapter.LoadOnDemandAsync(recording.Id, snapshot, 10_000, CancellationToken.None, null);
        var first = Assert.Single(client.Requests);
        Assert.True(first.PlaybackDelivery);
        Assert.False(string.IsNullOrWhiteSpace(first.PlaybackDeliveryId));

        await adapter.LoadOnDemandAsync(recording.Id, snapshot with { CursorSample = 9_000 }, 9_000,
            CancellationToken.None, null);
        Assert.Equal(2, client.Requests.Count);
        Assert.NotEqual(first.PlaybackDeliveryId, client.Requests[1].PlaybackDeliveryId);
        Assert.True(client.Requests[1].PlaybackGeneration > first.PlaybackGeneration);

        var oldDeliveryEvents = (IReviewTnkPlaybackEvents)adapter;
        var staleWindow = client.LastResponse! with
        {
            RequestId = $"review-tnk-event:{first.PlaybackDeliveryId}:1",
        };
        Assert.False(oldDeliveryEvents.IsCurrent(staleWindow));
        Assert.False(oldDeliveryEvents.IsCurrent(new TnkPlaybackGap(1, 1, 0, 1000, 0, 1,
            true, first.PlaybackDeliveryId)));

        await adapter.ReleaseOnDemandAsync();
        Assert.NotNull(client.ClosedDeliveryId);
    }

    [Fact]
    public async Task Playback_events_are_ordered_and_duplicate_sequences_or_scientific_windows_are_ignored()
    {
        var client = new FixtureClient
        {
            NextBatch = new TaskCompletionSource<TnkPlaybackEventBatchResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var adapter = new ReviewTnkAnalysisAdapter(client);
        var recording = await adapter.RegisterAsync("fixture", CancellationToken.None);
        var algorithm = await adapter.GetAlgorithmAsync(CancellationToken.None);
        var snapshot = ReviewAnalysisSnapshot.From(new AnalysisRunRequest(recording.Id,
            "official_algorithm", Config()), recording, 0, algorithm);
        var published = new List<string>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.WindowCompleted += response =>
        {
            published.Add(response.WindowId);
            if (published.Count == 2) received.TrySetResult();
        };

        await adapter.LoadOnDemandAsync(recording.Id, snapshot, 10_000, CancellationToken.None, null);
        await client.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var request = Assert.Single(client.Requests);
        var template = client.LastResponse!;
        var events = new[]
        {
            Frame(2, "window-2", 8000, 12000, request.PlaybackDeliveryId!, template),
            Frame(1, "window-1", 7000, 11000, request.PlaybackDeliveryId!, template),
            Frame(1, "window-1", 7000, 11000, request.PlaybackDeliveryId!, template),
            Frame(3, "window-1", 7000, 11000, request.PlaybackDeliveryId!, template),
        };
        client.NextBatch.TrySetResult(new TnkPlaybackEventBatchResponse(request.PlaybackDeliveryId!,
            events, 4, 3, false, "complete", null));

        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "window-1", "window-2" }, published);
        await adapter.ReleaseOnDemandAsync();
    }

    [Fact]
    public async Task Forward_cursor_request_keeps_bound_identity_while_next_window_is_computing()
    {
        var client = new FixtureClient
        {
            NextBatch = new TaskCompletionSource<TnkPlaybackEventBatchResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var adapter = new ReviewTnkAnalysisAdapter(client);
        var recording = await adapter.RegisterAsync("fixture", CancellationToken.None);
        var algorithm = await adapter.GetAlgorithmAsync(CancellationToken.None);
        var snapshot = ReviewAnalysisSnapshot.From(new AnalysisRunRequest(recording.Id,
            "official_algorithm", Config()), recording, 0, algorithm);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.WindowCompleted += _ => published.TrySetResult();

        await adapter.LoadOnDemandAsync(recording.Id, snapshot, 10_000, CancellationToken.None, null);
        await client.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var firstResponse = client.LastResponse!;
        client.PendingResponse = new TaskCompletionSource<DynamicWindowResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var nextLoad = adapter.LoadOnDemandAsync(recording.Id, snapshot with { CursorSample = 11_000 },
            11_000, CancellationToken.None, null);
        await client.SecondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var request = Assert.Single(client.Requests.Skip(1));
        var frame = Frame(1, "next-window", 7_000, 11_000, request.PlaybackDeliveryId!, firstResponse);
        client.NextBatch.TrySetResult(new TnkPlaybackEventBatchResponse(request.PlaybackDeliveryId!,
            [frame], 1, 1, false, "complete", null));

        await published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.PendingResponse.TrySetResult(client.LastResponse!);
        await nextLoad;
        await adapter.ReleaseOnDemandAsync();
    }

    private static JsonElement Config() => JsonSerializer.SerializeToElement(new
    {
        algorithm_id = "tnk_biofeedback", scientific_version = "tnk-fixture-v1",
        tnk_role_mapping = new { Fz = "Fz", Pz = "Pz", Oz = "Oz", F3 = "F3", F4 = "F4" },
    });

    private static TnkPlaybackFrameEventResponse Frame(long sequence, string windowId, long start, long end,
        string deliveryId, DynamicWindowResponse template)
    {
        var templateIdentity = template.Identity!.Value;
        var identity = JsonSerializer.SerializeToElement(new
        {
            recording_id = templateIdentity.GetProperty("recording_id").GetString(),
            source_sha256 = templateIdentity.GetProperty("source_sha256").GetString(),
            algorithm_id = templateIdentity.GetProperty("algorithm_id").GetString(),
            configuration_sha256 = templateIdentity.GetProperty("configuration_sha256").GetString(),
            start_sample = start,
            end_sample = end,
        });
        return new TnkPlaybackFrameEventResponse(sequence, "session", deliveryId, windowId,
            "scientific_identity_key", identity, start / 1000d, end / 1000d, 1000, template.Result!);
    }

    private sealed class FixtureClient : IAlgorithmClient, IRecordingRegistrationClient
    {
        public List<DynamicWindowRequest> Requests { get; } = [];
        public string? ClosedDeliveryId { get; private set; }
        public DynamicWindowResponse? LastResponse { get; private set; }
        public TaskCompletionSource PollStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TnkPlaybackEventBatchResponse>? NextBatch { get; init; }
        public TaskCompletionSource<DynamicWindowResponse>? PendingResponse { get; set; }
        public TaskCompletionSource SecondRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly RegisteredRecording recording = new("recording", "fixture", ".wpf", "now", 1000,
            100, ["Fz", "Pz", "Oz", "F3", "F4"], "source-hash");
        private readonly AlgorithmCatalogItem algorithm = new("official", "tnk_biofeedback", "tnk-fixture-v1",
            "TNK", "TNK", "", [], ["dynamic"], default,
            new DynamicAnalysisPolicy(4, [4], 4, 1, true), "available", true,
            "tnk-definition", "1", "tnk-implementation",
            JsonSerializer.SerializeToElement(new { on_demand = new { implementation = "implemented" } }));

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlgorithmCatalogItem>>([algorithm]);

        public Task<RegisteredRecording> RegisterWpfRecordingAsync(string directory,
            CancellationToken cancellationToken, IReadOnlyList<MontageProfile>? montageProfiles = null) =>
            Task.FromResult(recording);

        public Task<DynamicWindowResponse> EnsureDynamicWindowAsync(DynamicWindowRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var canonical = JsonSerializer.Serialize(new
            {
                scientific_configuration_contract = "scientific-config-v1",
                config = request.Config,
                context = new { },
            });
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            var identity = JsonSerializer.SerializeToElement(new
            {
                recording_id = recording.Id, source_sha256 = recording.SourceSha256,
                algorithm_id = algorithm.Id, configuration_sha256 = digest,
                start_sample = 6000L, end_sample = 10000L,
            });
            var response = new DynamicWindowResponse("session", "window", request.RequestId!, "ready",
                identity, new(10000, 6000, 10000, 6, 10, "planned", null),
                new(6000, 10000, 6, 10), "clean", null,
                new(identity, "ready", "algorithm_group", null, null, null, null,
                    "clean", null, null, null),
                ScientificConfigurationJson: canonical);
            LastResponse = response;
            if (Requests.Count == 2)
            {
                SecondRequestStarted.TrySetResult();
                if (PendingResponse is not null) return PendingResponse.Task;
            }
            return Task.FromResult(response);
        }

        public Task<TnkPlaybackEventBatchResponse> GetTnkPlaybackEventsAsync(string deliveryId, long afterSequence,
            string? projectId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PollStarted.TrySetResult();
            if (NextBatch is not null) return NextBatch.Task.WaitAsync(cancellationToken);
            return Task.FromResult(new TnkPlaybackEventBatchResponse(deliveryId, [], afterSequence,
                afterSequence, false, "complete", null));
        }

        public Task CloseTnkPlaybackEventsAsync(string deliveryId, string? projectId,
            CancellationToken cancellationToken)
        {
            ClosedDeliveryId = deliveryId;
            return Task.CompletedTask;
        }

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

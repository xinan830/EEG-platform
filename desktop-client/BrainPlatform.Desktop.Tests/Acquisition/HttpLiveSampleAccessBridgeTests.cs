using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Acquisition.Runtime;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class HttpLiveSampleAccessBridgeTests
{
    [Fact]
    public async Task ProjectsSelectedResultsInOrderAndUploadsOnlyEegVolts()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var updates = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => updates.Add(update);
        var batch = CreateBatch();

        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Cz", "Fz"]), CancellationToken.None);
        await bridge.PublishAsync(batch, CancellationToken.None);

        var result = LiveRbpPresentation.Read(updates.Single(update => update.State == "ready"), ["Cz", "Fz"])!;
        Assert.Equal(batch.SessionId, result.SessionId);
        Assert.Equal(new[] { "Cz", "Fz" }, result.Channels!.Select(channel => channel.Channel));
        Assert.Equal(new[] { 1d, 2d, 3d, 4d }, handler.UploadedValues);
        Assert.True(result.Revision > updates[0].Revision);
    }

    [Fact]
    public async Task ClosingSessionClearsRbpEnablementBeforeSessionReplacement()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        await bridge.PublishAsync(CreateBatch(), CancellationToken.None);
        await bridge.CloseSessionAsync(CancellationToken.None);

        await bridge.PublishAsync(CreateBatch(), CancellationToken.None);

        Assert.Equal(1, handler.Starts);
        Assert.Equal(1, handler.Closes);
    }

    [Fact]
    public async Task DisableWaitsForAnInFlightUploadThenPreventsFurtherResults()
    {
        using var handler = new LiveHandler { HoldUpload = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var results = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => results.Add(update);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        var batch = CreateBatch();
        var publish = bridge.PublishAsync(batch, CancellationToken.None);
        await handler.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disable = bridge.SetAlgorithmEnabledAsync("rbp", false, default, CancellationToken.None);
        Assert.False(disable.IsCompleted);
        handler.ReleaseUpload.TrySetResult();
        await publish;
        await disable;
        var count = results.Count;

        await bridge.PublishAsync(batch, CancellationToken.None);

        Assert.Equal(count, results.Count);
    }

    [Fact]
    public async Task EnablingUsesExistingSessionAndDeliversFirstResultWithoutAnotherUpload()
    {
        using var handler = new LiveHandler { LatestEndSeconds = 23 };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var resultReady = new TaskCompletionSource<LiveAnalysisUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.AnalysisUpdated += (_, update) =>
        {
            if (update.State == "ready") resultReady.TrySetResult(update);
        };
        var batch = CreateBatch(samplingRateHz: 4096);
        await bridge.PublishAsync(batch, CancellationToken.None);
        Assert.Equal(0, handler.Starts);
        Assert.True(handler.CapacitySamples >= batch.Stream.SamplingRateHz * 10);

        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        Assert.Equal(1, handler.Starts);
        var update = await resultReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result = LiveRbpPresentation.Read(update, ["Fz"])!;
        Assert.Equal((13d, 23d), (result.StartSeconds, result.EndSeconds));
        Assert.Equal(1, handler.Uploads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingOrClosingCancelsAnInFlightFirstResult(bool closeSession)
    {
        using var handler = new LiveHandler { LatestEndSeconds = 23, HoldLatest = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        bridge.AnalysisUpdated += (_, update) => Assert.Equal("warming_up", update.State);
        var batch = CreateBatch();
        await bridge.PublishAsync(batch, CancellationToken.None);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        await handler.LatestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        if (closeSession) await bridge.CloseSessionAsync(CancellationToken.None);
        else await bridge.SetAlgorithmEnabledAsync("rbp", false, default, CancellationToken.None);

        await handler.LatestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(bridge.HasEnabledAlgorithms);
        Assert.Equal(closeSession ? 1 : 0, handler.Closes);
    }

    private static AcquisitionAnalysisBatch CreateBatch(int samplingRateHz = 512, long firstSampleCounter = 0, Guid? sessionId = null) => new(
        sessionId ?? Guid.NewGuid(),
        new AcquisitionStreamMetadata("device", "device", samplingRateHz,
            [new AcquisitionChannel(0, 0, "Fz", AcquisitionChannelKind.Reference, "V"),
             new AcquisitionChannel(1, 1, "Cz", AcquisitionChannelKind.Reference, "V"),
             new AcquisitionChannel(2, 2, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
            0, DateTimeOffset.UtcNow),
        new AcquisitionBatch(firstSampleCounter, 2, 3, [1, 2, 998, 3, 4, 999], DateTimeOffset.UtcNow), null, "");

    private static LiveMontageSnapshot Montage(long revision, string name, string positive)
    {
        var now = DateTimeOffset.UnixEpoch;
        var channels = new ChannelConfigurationProfile("channels", "Channels", "", ChannelConfigurationSource.User,
            "device", [], now, now);
        var profile = new MontageProfile("montage", "Montage", "", MontageProfileSource.User, "device", "config",
            channels, [new DerivedMontageChannel(name, positive, MontageNegativeKind.OriginalHardwareReference, [], 0)],
            1, now, now, $"fingerprint-{revision}");
        return new LiveMontageSnapshot(profile, revision, revision == 1 ? 0 : 2, [name], profile.Fingerprint);
    }

    [Fact]
    public async Task RoutesMultipleAlgorithmsAndStopsOnlyTheRequestedOne()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var updates = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => updates.Add(update);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        await bridge.SetAlgorithmEnabledAsync("second", true, JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
        var batch = CreateBatch();
        await bridge.PublishAsync(batch, CancellationToken.None);
        Assert.Equal(new[] { "rbp", "second" }, updates.Where(update => update.State == "ready").Select(update => update.AlgorithmId));
        Assert.True(bridge.HasEnabledAlgorithms);
        await bridge.SetAlgorithmEnabledAsync("rbp", false, default, CancellationToken.None);
        updates.Clear();
        await bridge.PublishAsync(batch, CancellationToken.None);
        Assert.Equal("second", Assert.Single(updates).AlgorithmId);
        Assert.True(bridge.HasEnabledAlgorithms);
    }

    [Fact]
    public async Task TypedStartFailureDoesNotStopAnotherAlgorithmOrSampleUpload()
    {
        using var handler = new LiveHandler { RejectAlgorithm = "broken" };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var updates = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => updates.Add(update);
        await bridge.SetAlgorithmEnabledAsync("broken", true, JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        await bridge.PublishAsync(CreateBatch(), CancellationToken.None);
        Assert.Contains(updates, update => update.AlgorithmId == "broken" && update.FailureCode == "LIVE_CONFIG_INVALID");
        Assert.Contains(updates, update => update.AlgorithmId == "rbp" && update.State == "ready");
        Assert.NotEmpty(handler.UploadedValues);
    }

    [Fact]
    public async Task RejectsStaleSessionAcknowledgements()
    {
        using var handler = new LiveHandler { WrongSession = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.PublishAsync(CreateBatch(), CancellationToken.None));
        Assert.Contains("LIVE_SAMPLE_ACK_INVALID", error.Message);
    }

    [Fact]
    public async Task UnknownAlgorithmsAndOldRevisionsCannotReachPresenters()
    {
        using var handler = new LiveHandler { AddUnknown = true, RepeatRevision = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var updates = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => updates.Add(update);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz"]), CancellationToken.None);
        var batch = CreateBatch();
        await bridge.PublishAsync(batch, CancellationToken.None);
        await bridge.PublishAsync(batch, CancellationToken.None);
        Assert.Single(updates, update => update.State == "ready");
        Assert.All(updates, update => Assert.Equal("rbp", update.AlgorithmId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.PublishAsync(CreateBatch(), CancellationToken.None));
    }

    [Fact]
    public async Task MontageRevisionUpdatesBackendAndRestartsAlgorithmWithProjectedChannelNames()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var config = JsonSerializer.SerializeToElement(new
        {
            channels = new[] { "Fz" },
            positions = new Dictionary<string, object> { ["Fz"] = new { x = -0.3, y = 0.4 } },
        });
        await bridge.SetAlgorithmEnabledAsync("rbp", true, config, CancellationToken.None);

        var session = Guid.NewGuid();
        await bridge.PublishAsync(CreateBatch(sessionId: session) with { LiveMontage = Montage(1, "Fz-REF", "Fz") }, CancellationToken.None);
        await bridge.PublishAsync(CreateBatch(firstSampleCounter: 2, sessionId: session) with { LiveMontage = Montage(2, "Fz-Cz", "Fz") }, CancellationToken.None);

        Assert.Equal(2, handler.MontageUpdates);
        Assert.Equal(2, handler.Starts);
        Assert.Equal("Fz-Cz", Assert.Single(handler.LastAlgorithmChannels));
        Assert.Equal("Fz-Cz", Assert.Single(handler.LastAlgorithmPositions));
    }

    [Fact]
    public async Task UnmappableAlgorithmChannelIsReportedWithoutBlockingRawSampleUpload()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var updates = new List<LiveAnalysisUpdate>();
        bridge.AnalysisUpdated += (_, update) => updates.Add(update);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["F9"]), CancellationToken.None);

        await bridge.PublishAsync(CreateBatch() with { LiveMontage = Montage(1, "Fz-REF", "Fz") }, CancellationToken.None);

        Assert.Contains(updates, update => update.FailureCode == "LIVE_MONTAGE_CHANNEL_UNAVAILABLE");
        Assert.NotEmpty(handler.UploadedValues);
        Assert.Equal(0, handler.Starts);
    }

    [Fact]
    public async Task PendingMontageDefersAlgorithmRestartUntilNextBatchSyncsBackend()
    {
        using var handler = new LiveHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var bridge = new HttpLiveSampleAccessBridge(client);
        var session = Guid.NewGuid();
        var initial = Montage(1, "Fz-REF", "Fz");
        var switched = Montage(2, "Cz-REF", "Cz");

        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Fz-REF"]), CancellationToken.None);
        await bridge.PublishAsync(CreateBatch(sessionId: session) with { LiveMontage = initial }, CancellationToken.None);
        Assert.Equal(1, handler.Starts);

        bridge.NotifyPendingMontage(switched);
        await bridge.SetAlgorithmEnabledAsync("rbp", false, default, CancellationToken.None);
        await bridge.SetAlgorithmEnabledAsync("rbp", true, LiveRbpPresentation.Config(["Cz-REF"]), CancellationToken.None);
        Assert.Equal(1, handler.Starts);

        await bridge.PublishAsync(CreateBatch(firstSampleCounter: 2, sessionId: session) with { LiveMontage = switched }, CancellationToken.None);
        Assert.Equal(2, handler.MontageUpdates);
        Assert.Equal(2, handler.Starts);
        Assert.Equal("Cz-REF", Assert.Single(handler.LastAlgorithmChannels));
    }

    private sealed class LiveHandler : HttpMessageHandler
    {
        private string sessionId = "";
        private readonly Dictionary<string, string[]> algorithms = [];
        private long revision;
        public int Starts { get; private set; }
        public int Closes { get; private set; }
        public int Uploads { get; private set; }
        public int MontageUpdates { get; private set; }
        public string[] LastAlgorithmChannels { get; private set; } = [];
        public string[] LastAlgorithmPositions { get; private set; } = [];
        public int CapacitySamples { get; private set; }
        private int samplingRateHz;
        public int? LatestEndSeconds { get; init; }
        public bool HoldLatest { get; init; }
        public TaskCompletionSource LatestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LatestCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public double[] UploadedValues { get; private set; } = [];
        public bool HoldUpload { get; init; }
        public bool WrongSession { get; init; }
        public bool AddUnknown { get; init; }
        public bool RepeatRevision { get; init; }
        public string? RejectAlgorithm { get; init; }
        public TaskCompletionSource UploadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseUpload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.EndsWith("/montage")) MontageUpdates++;
            if (request.Method == HttpMethod.Post && path.EndsWith("/sessions"))
            {
                using var document = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                sessionId = document.RootElement.GetProperty("session_id").GetString()!;
                samplingRateHz = document.RootElement.GetProperty("sampling_rate_hz").GetInt32();
                CapacitySamples = document.RootElement.GetProperty("capacity_samples").GetInt32();
            }
            if (path.Contains("/algorithms/"))
            {
                var algorithm = path.Split('/')[^1];
                if (request.Method == HttpMethod.Post)
                {
                    Starts++;
                    if (algorithm == RejectAlgorithm)
                        return new(HttpStatusCode.UnprocessableEntity) { Content = JsonContent.Create(new { code = "LIVE_CONFIG_INVALID", message = "测试配置无效", request_id = "test" }) };
                    using var document = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    Assert.Equal("live-analysis-config-v1", document.RootElement.GetProperty("schema_version").GetString());
                    var config = document.RootElement.GetProperty("config");
                    algorithms[algorithm] = config.TryGetProperty("channels", out var names)
                        ? names.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
                    LastAlgorithmChannels = algorithms[algorithm];
                    LastAlgorithmPositions = config.TryGetProperty("positions", out var positions)
                        ? positions.EnumerateObject().Select(item => item.Name).ToArray() : [];
                    return Ok(Envelope(algorithm, "warming_up", 0, null));
                }
                if (request.Method == HttpMethod.Get)
                {
                    if (HoldLatest)
                    {
                        LatestStarted.TrySetResult();
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                        finally { LatestCancelled.TrySetResult(); }
                    }
                    return LatestEndSeconds is { } end
                        ? Ok(Envelope(algorithm, "ready", 1, RbpPayload(algorithms[algorithm], end), end))
                        : Ok(Envelope(algorithm, "warming_up", 0, null));
                }
                algorithms.Remove(algorithm);
            }
            else if (request.Method == HttpMethod.Delete) { Closes++; algorithms.Clear(); }
            if (path.EndsWith("/batches/binary"))
            {
                Uploads++;
                var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                UploadedValues = MemoryMarshal.Cast<byte, double>(bytes).ToArray();
                UploadStarted.TrySetResult();
                if (HoldUpload) await ReleaseUpload.Task.WaitAsync(cancellationToken);
                revision = RepeatRevision ? 1 : revision + 1;
                var updates = algorithms.Select(item => Envelope(item.Key, "ready", revision,
                    item.Key == "rbp" ? RbpPayload(item.Value) : JsonSerializer.SerializeToElement(new { value = 1 }))).ToList();
                if (AddUnknown) updates.Add(Envelope("unregistered", "ready", revision, JsonSerializer.SerializeToElement(new { unknown = true })));
                return Ok(new { type = "live_samples_accepted", schema_version = "live-sample-ack-v2",
                    session_id = WrongSession ? Guid.NewGuid().ToString("N") : sessionId, updates });
            }
            return Ok(new { });
        }

        private object Envelope(string algorithm, string state, long resultRevision, JsonElement? payload, int end = 10) => new
        {
            type = "live_analysis_update", schema_version = "live-analysis-update-v2", algorithm_id = algorithm,
            acquisition_session_id = sessionId, revision = resultRevision, state,
            scientific_identity = algorithm == "rbp" ? "rbp-live-v1" : "test-v1",
            start_sample = payload is null ? (long?)null : (end - 10) * samplingRateHz,
            end_sample = payload is null ? (long?)null : end * samplingRateHz,
            start_s = payload is null ? (double?)null : end - 10, end_s = payload is null ? (double?)null : end,
            payload, failure = (object?)null,
        };

        private JsonElement RbpPayload(string[] channels, int end = 10)
        {
            var bands = new[] { "delta", "theta", "alpha", "beta", "gamma" };
            return JsonSerializer.SerializeToElement(new
            {
                type = "live_rbp_result", schema_version = "live-rbp-result-v1", algorithm_id = "rbp",
                scientific_identity = "rbp-live-v1", acquisition_session_id = sessionId, state = "ready", channels,
                start_sample = (end - 10) * samplingRateHz, end_sample = end * samplingRateHz, start_s = end - 10, end_s = end,
                values = bands.ToDictionary(band => band, _ => channels.Select(_ => 0.2).ToArray()),
                units = bands.ToDictionary(band => band, _ => "ratio"),
                quality = channels.ToDictionary(channel => channel, _ => new { status = "clean", reasons = Array.Empty<string>() })
            });
        }

        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}

using System.Net;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpAlgorithmClientTests
{
    [Fact]
    public async Task ListAlgorithms_UsesBackendCatalogWithoutClientSideDefaults()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                algorithms = new[]
                {
                    new
                    {
                        source = "official", id = "psd", version = "official-psd-v1",
                        display_name_zh = "功率谱密度", abbreviation = "PSD", description = "频谱",
                        parameters = Array.Empty<object>(), modes = new[] { "static" },
                        output_schema = new { kind = "frequency_series" },
                        dynamic_policy = new { minimum_window_s = 4.0, window_options_s = new[] { 10.0 }, default_window_s = 10.0, refresh_step_s = 1.0, allow_warmup = false },
                        availability = "available", is_runnable = true,
                        definition_id = "official-psd", definition_version = "1", implementation_identity = "psd-v1",
                    },
                },
            }),
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var items = await api.ListAlgorithmsAsync(CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("psd", item.Id);
        Assert.Equal("official-psd-v1", item.Version);
        Assert.Equal(10.0, item.DynamicPolicy.DefaultWindowSeconds);
    }

    [Fact]
    public async Task GetRun_InvalidResponseExposesStructuredBackendError()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { code = "REQUEST_INVALID", message = "分析范围无效" }),
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var error = await Assert.ThrowsAsync<AlgorithmApiException>(
            () => api.GetRunAsync("run-1", CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Equal("REQUEST_INVALID", error.Code);
        Assert.Equal("分析范围无效", error.Message);
    }

    [Fact]
    public async Task RegisterWpfRecording_SendsTheCompletedRecordingDirectory()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "rec-1", original_name = "recording", extension = ".wpf",
                    created_at = "2026-09-26T00:00:00Z", sfreq = 2000.0,
                    duration_s = 2.0, channels = new[] { "Fz" }, source_sha256 = "abc",
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var recording = await api.RegisterWpfRecordingAsync("D:\\recordings\\rec-1", CancellationToken.None);

        Assert.Equal("rec-1", recording.Id);
        Assert.NotNull(captured);
        Assert.Equal("/api/recordings/register-wpf", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("rec-1", capturedBody);
    }

    [Fact]
    public async Task StructuredPreview_DeserializesBackendOwnedAxesAndArrays()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/api/runs/run-1/structured-preview", request.RequestUri!.AbsolutePath);
            Assert.Contains("max_cells=4000", request.RequestUri.Query);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    run_id = "run-1",
                    artifact = new
                    {
                        artifact_id = "artifact-1", run_id = "run-1", kind = "psd",
                        relative_path = "psd.npz", media_type = "application/octet-stream",
                        byte_size = 12, sha256 = "abc", unit = "V^2/Hz",
                        shape = new Dictionary<string, int[]> { ["psd"] = new[] { 1, 3 } },
                        created_at = "2026-09-26T00:00:00Z",
                    },
                    output = new { kind = "frequency_series" },
                    channel_order = new[] { "Fz" },
                    requested_range = new { start_s = 0.0, end_s = 10.0 },
                    actual_range = new { start_s = 0.0, end_s = 10.0 },
                    axes = new { frequency_hz = new[] { 1.0, 2.0, 3.0 } },
                    axis_metadata = new { frequency_hz = new { unit = "Hz" } },
                    arrays = new { psd = new[] { 1.0, 2.0, 3.0 } },
                    array_metadata = new { psd = new { unit = "V^2/Hz" } },
                    windows = Array.Empty<object>(),
                    window_state_counts = new { Complete = 1 },
                    quality = new { status = "clean" },
                    scientific_version = "official-psd-v1",
                    implementation_version = "impl-1",
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var preview = await api.GetStructuredPreviewAsync("run-1", 4000, CancellationToken.None);

        Assert.Equal("run-1", preview.RunId);
        Assert.Equal("Fz", Assert.Single(preview.ChannelOrder));
        Assert.Contains("frequency_hz", preview.Axes.Keys);
        Assert.Single(preview.WindowStateCounts);
        Assert.Equal(1, preview.WindowStateCounts.Values.Single());
    }

    [Fact]
    public async Task GetRun_PreservesNullResultSummaryAndProvenance()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                run_id = "run-1", recording_id = "rec-1", analysis_type = "official_algorithm",
                status = "gate_failed", scientific_version = "official-psd-v1",
                requested_range = new { start_s = 0.0, end_s = 10.0 }, actual_range = (object?)null,
                result_summary = (object?)null, analysis_provenance = (object?)null, error = new
                {
                    code = "PSD_QUALITY_GATE_FAILED", message = "质量门失败", stage = "quality", details = new { },
                },
                created_at = "2026-09-26T00:00:00Z", updated_at = "2026-09-26T00:00:01Z",
            }),
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var run = await api.GetRunAsync("run-1", CancellationToken.None);

        Assert.Equal("gate_failed", run.Status);
        Assert.False(run.ResultSummary.HasValue);
        Assert.False(run.AnalysisProvenance.HasValue);
        Assert.Equal("PSD_QUALITY_GATE_FAILED", run.Error!.Code);
    }

    [Fact]
    public async Task AlgorithmListViewModel_PollsQueuedRunUntilTerminalState()
    {
        var client = new PollingClient();
        var viewModel = new AlgorithmListViewModel(client, new OperationNotificationCenter());

        var result = await viewModel.PollRunToTerminalAsync(
            Run("queued"), "功率谱密度", CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, client.PollCount);
        Assert.Contains("completed", viewModel.RunStatusText);
    }

    private static AnalysisRunResponse Run(string status) => new(
        "run-1", "rec-1", "official_algorithm", status, "official-psd-v1", null, null,
        null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class PollingClient : IAlgorithmClient
    {
        public int PollCount { get; private set; }

        public Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> CreateRunAsync(AnalysisRunRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            PollCount++;
            return Task.FromResult(Run("completed"));
        }

        public Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(string runId, int maxCells, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

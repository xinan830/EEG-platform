using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpAlgorithmClientTests
{
    [Fact]
    public async Task SignalPreviewPostsMontageChannelsAndAbsoluteTime()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/api/recordings/source/signal-preview", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal(20, body.RootElement.GetProperty("start_s").GetDouble());
            Assert.Equal("montage", body.RootElement.GetProperty("montage_id").GetString());
            Assert.Equal("Pz-AVG", body.RootElement.GetProperty("channels")[0].GetString());
            Assert.Equal(2, body.RootElement.GetProperty("low_hz").GetDouble());
            Assert.Equal(20, body.RootElement.GetProperty("high_hz").GetDouble());
            Assert.Equal(50, body.RootElement.GetProperty("notch_hz").GetDouble());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { recording_id = "source", montage_id = "montage", unit = "uV", filter = "display_causal_iir", low_hz=2d,high_hz=20d,notch_hz=50d,
              elapsed_s = new[] {20d,20.001}, channels = new Dictionary<string,double[]> { ["Pz-AVG"] = [1,2] },
              extrema_elapsed_s = new[] {20.0005}, channel_extrema = new Dictionary<string, object>
              { ["Pz-AVG"] = new { minimum_uv = new[] {-2d}, maximum_uv = new[] {3d} } } }) };
        })) { BaseAddress = new Uri("http://localhost/") };
        var result = await new HttpAlgorithmClient(client).GetSignalPreviewAsync("source", new(20,10,"montage",["Pz-AVG"],LowHz:2,HighHz:20,NotchHz:50),CancellationToken.None);
        Assert.Equal("uV", result.Unit);
        Assert.Equal(20, result.TimeSeconds[0]);
        Assert.Equal(2,result.LowHz); Assert.Equal(20,result.HighHz); Assert.Equal(50,result.NotchHz);
        Assert.Equal(20.0005, Assert.Single(result.ExtremaTimeSeconds!));
        Assert.Equal(-2, Assert.Single(result.ChannelExtrema!["Pz-AVG"].MinimumMicrovolts));
        Assert.Equal(3, Assert.Single(result.ChannelExtrema["Pz-AVG"].MaximumMicrovolts));
    }
    [Theory]
    [InlineData("{\"detail\":\"分析范围无效\"}", "分析范围无效")]
    [InlineData("{\"detail\":[{\"loc\":[\"config\",\"high_hz\"],\"msg\":\"maximum 30\"}]}", "high_hz")]
    [InlineData("{\"detail\":{\"message\":\"invalid band\"}}", "invalid band")]
    [InlineData("{\"detail\":null}", "HTTP 422")]
    [InlineData("{\"code\":42,\"message\":[]}", "HTTP 422")]
    [InlineData("[]", "HTTP 422")]
    [InlineData("<html>proxy error</html>", "HTTP 422")]
    public async Task ErrorShapes_DoNotMaskOriginalHttpFailure(string body, string expected)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(body),
        })) { BaseAddress = new Uri("http://localhost/") };
        var error = await Assert.ThrowsAsync<AlgorithmApiException>(() =>
            new HttpAlgorithmClient(client).GetRunAsync("run-1", CancellationToken.None));
        Assert.Equal("REQUEST_INVALID", error.Code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Contains(expected, error.Message);
    }

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
    public async Task Requests_IncludeCorrelationIdWithoutLoggingPayload()
    {
        HttpRequestMessage? captured = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { algorithms = Array.Empty<object>() }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        await new HttpAlgorithmClient(client).ListAlgorithmsAsync(CancellationToken.None);

        Assert.NotNull(captured);
        var requestId = captured!.Headers.GetValues("X-Request-ID").Single();
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        Assert.Null(captured.Content);
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
                    duration_s = 2.0, channels = new[] { "F7-REF", "F3-REF" }, source_sha256 = "abc",
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };
        var api = new HttpAlgorithmClient(client);

        var recording = await api.RegisterWpfRecordingAsync("D:\\recordings\\rec-1", CancellationToken.None);

        Assert.Equal("rec-1", recording.Id);
        Assert.NotNull(captured);
        Assert.Equal("/api/recordings/register-wpf", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("rec-1", capturedBody);
        using var requestJson = JsonDocument.Parse(capturedBody!);
        Assert.Equal("acquisition_montage", requestJson.RootElement.GetProperty("signal_mode").GetString());
        Assert.Equal(new[] { "F7-REF", "F3-REF" }, recording.Channels);
    }

    [Fact]
    public async Task RegisterWpfRecording_SendsCompleteAnalysisMontageProfiles()
    {
        string? capturedBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "rec-1", original_name = "recording", extension = ".wpf",
                    created_at = "2026-09-26T00:00:00Z", sfreq = 2000.0,
                    duration_s = 2.0, channels = new[] { "F3-REF" }, source_sha256 = "abc",
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };
        var channelConfiguration = new ChannelConfigurationProfile(
            "channels-1", "Channels", "test", ChannelConfigurationSource.User, "device-1",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var profile = new MontageProfile(
            "montage-2", "Average", "test", MontageProfileSource.User, "device-1", "channels-fingerprint",
            channelConfiguration,
            [new DerivedMontageChannel("F3-AVG", "F3", MontageNegativeKind.Mean, ["F3", "F7"], 0)],
            2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            AverageReferenceLabels: ["F3", "F7"]);

        await new HttpAlgorithmClient(client).RegisterWpfRecordingAsync(
            "D:\\recordings\\rec-1", CancellationToken.None, [profile]);

        using var requestJson = JsonDocument.Parse(capturedBody!);
        var submitted = requestJson.RootElement.GetProperty("analysis_montage_profiles")[0];
        Assert.Equal("montage-2", submitted.GetProperty("id").GetString());
        Assert.Equal(2, submitted.GetProperty("revision").GetInt32());
        Assert.Equal("channels-1", submitted.GetProperty("channelConfigurationSnapshot").GetProperty("id").GetString());
        Assert.Equal(0, submitted.GetProperty("channelConfigurationSnapshot").GetProperty("channels")[0]
            .GetProperty("nativeChannelIndex").GetInt32());
        Assert.Equal("F3-AVG", submitted.GetProperty("derivedChannels")[0].GetProperty("name").GetString());
        Assert.Equal(2, submitted.GetProperty("derivedChannels")[0].GetProperty("negativeKind").GetInt32());
        Assert.Equal("F7", submitted.GetProperty("averageReferenceLabels")[1].GetString());
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

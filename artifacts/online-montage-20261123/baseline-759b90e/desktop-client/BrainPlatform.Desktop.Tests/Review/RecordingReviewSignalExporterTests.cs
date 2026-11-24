using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Review.Export;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class RecordingReviewSignalExporterTests
{
    [Fact]
    public async Task Provided_recording_exports_through_real_http_client_when_acceptance_is_configured()
    {
        var directory = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_EXPORT_RECORDING");
        var output = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_EXPORT_OUTPUT");
        var endpoint = Environment.GetEnvironmentVariable("BRAIN_PLATFORM_EXPORT_BACKEND");
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(endpoint))
            throw Xunit.Sdk.SkipException.ForSkip("Configure recording, output and backend to run real HTTP acceptance.");
        Directory.CreateDirectory(output);
        using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
        var client = new HttpAlgorithmClient(http);
        foreach (var format in new[] { "edf", "bdf" })
        {
            var target = Path.Combine(output, "desktop-http." + format);
            await RecordingReviewSignalExporter.ExportAsync(target, format,
                (stream, token) => client.DownloadRawSignalExportAsync(directory, format, stream, token), CancellationToken.None);
            Assert.True(new FileInfo(target).Length > 1000000);
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(target + ".labels.json"));
            Assert.Equal(149982, metadata.RootElement.GetProperty("sample_count").GetInt32());
            Assert.Equal(28, metadata.RootElement.GetProperty("channels").GetArrayLength());
        }
    }

    [Theory]
    [InlineData("edf")]
    [InlineData("bdf")]
    public async Task Registers_raw_source_and_publishes_only_expected_package_members(string format)
    {
        using var handler = new ExportHandler(format);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new HttpAlgorithmClient(http);
        var root = Directory.CreateTempSubdirectory("export-test-").FullName;
        try
        {
            var target = Path.Combine(root, "signal." + format);
            await File.WriteAllTextAsync(target, "old signal");
            await File.WriteAllTextAsync(target + ".labels.json", "old metadata");
            await RecordingReviewSignalExporter.ExportAsync(target, format,
                (stream, token) => client.DownloadRegisteredSignalAsync("source-path", format, stream, token), CancellationToken.None);
            Assert.Equal("signal samples", await File.ReadAllTextAsync(target));
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(target + ".labels.json"));
            Assert.Equal("recording-signal-export-v2", metadata.RootElement.GetProperty("schema_version").GetString());
            Assert.Equal(2, Directory.GetFiles(root).Length);
            Assert.Empty(Directory.GetDirectories(root));
            Assert.Equal(2, handler.Calls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("bad-package")]
    [InlineData("cancel")]
    [InlineData("publish-failure")]
    public async Task Failure_or_cancellation_preserves_existing_destination(string problem)
    {
        var root = Directory.CreateTempSubdirectory("export-test-").FullName;
        var target = Path.Combine(root, "signal.edf");
        await File.WriteAllTextAsync(target, "original");
        if (problem == "publish-failure") Directory.CreateDirectory(target + ".labels.json");
        else await File.WriteAllTextAsync(target + ".labels.json", "original metadata");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => RecordingReviewSignalExporter.ExportAsync(target, "edf", async (stream, token) =>
            {
                if (problem == "cancel") throw new OperationCanceledException();
                if (problem == "bad-package") await stream.WriteAsync(new byte[] { 1, 2 }, token);
                else await stream.WriteAsync(Package("edf"), token);
            }, CancellationToken.None));
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            if (problem != "publish-failure")
                Assert.Equal("original metadata", await File.ReadAllTextAsync(target + ".labels.json"));
            Assert.DoesNotContain(Directory.GetDirectories(root), path => Path.GetFileName(path).StartsWith(".signal-export-"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Backend_structured_failure_is_preserved()
    {
        using var http = new HttpClient(new ExportHandler("edf", fail: true)) { BaseAddress = new Uri("http://localhost/") };
        var client = new HttpAlgorithmClient(http);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<BrainPlatform.Desktop.Modules.Algorithms.Contracts.AlgorithmApiException>(() =>
            client.DownloadRegisteredSignalAsync("source-path", "edf", output, CancellationToken.None));
        Assert.Equal("RECORDING_EXPORT_BUSY", error.Code);
        Assert.Equal(0, output.Length);
    }

    private static byte[] Package(string format)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("recording." + format).Open())) writer.Write("signal samples");
            using (var writer = new StreamWriter(archive.CreateEntry("recording." + format + ".labels.json").Open()))
                writer.Write("{\"schema_version\":\"recording-signal-export-v2\"}");
        }
        return stream.ToArray();
    }

    private sealed class ExportHandler(string format, bool fail = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (Calls == 1)
            {
                Assert.Equal("/api/recordings/register-wpf", request.RequestUri!.AbsolutePath);
                Assert.Equal("raw", body.RootElement.GetProperty("signal_mode").GetString());
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(
                    "{\"id\":\"raw-id\",\"original_name\":\"test\",\"extension\":\".wpf\",\"created_at\":\"2026-11-05\",\"channels\":[\"Fp1\"]}", Encoding.UTF8, "application/json") };
            }
            Assert.Equal("/api/recordings/raw-id/signal-export", request.RequestUri!.AbsolutePath);
            Assert.Equal(format, body.RootElement.GetProperty("format").GetString());
            if (fail) return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(
                "{\"code\":\"RECORDING_EXPORT_BUSY\",\"message\":\"busy\"}", Encoding.UTF8, "application/json") };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package(format)) };
            response.Content.Headers.ContentType = new("application/zip");
            return response;
        }
    }
}

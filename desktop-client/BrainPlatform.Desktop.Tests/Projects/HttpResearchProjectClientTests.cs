using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class HttpResearchProjectClientTests
{
    [Fact]
    public async Task EnsureProjectUsesStableIdAndExplicitJsonNames()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { project_id = "project-1", name = "Study", description = "desc",
                    created_at = "2026-01-01T00:00:00Z", updated_at = "2026-01-01T00:00:00Z" }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        var result = await new HttpResearchProjectClient(http).EnsureProjectAsync("project-1", "Study", "desc", CancellationToken.None);

        Assert.Equal("project-1", result.ProjectId);
        Assert.Equal("/api/projects/desktop/project-1", captured!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("Study", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("desc", body.RootElement.GetProperty("description").GetString());
        Assert.False(body.RootElement.TryGetProperty("Name", out _));
        Assert.Single(captured.Headers.GetValues("X-Request-ID"));
    }

    [Fact]
    public async Task CreateSessionSendsSubjectAndRecordingIds()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("subject-1", body.RootElement.GetProperty("subject_id").GetString());
            Assert.Equal("recording-1", body.RootElement.GetProperty("recording_id").GetString());
            Assert.True(body.RootElement.TryGetProperty("condition_id", out _));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { session_id = "session-1", project_id = "project-1",
                    subject_id = "subject-1", recording_id = "recording-1", condition_id = (string?)null,
                    label = "", created_at = "2026-01-01T00:00:00Z" }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        var result = await new HttpResearchProjectClient(http).CreateSessionAsync("project-1",
            new CreateResearchSessionRequest("subject-1", "recording-1"), CancellationToken.None);

        Assert.Equal("session-1", result.SessionId);
    }

    [Fact]
    public async Task DeleteImportedRecordingDeletesProjectSessionAndReadsStorageOutcome()
    {
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { session_id = "session-1", recording_id = "recording-1", recording_deleted = true }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        var result = await new HttpResearchProjectClient(http).DeleteImportedRecordingAsync(
            "project-1", "session-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.Equal("/api/projects/project-1/sessions/session-1", captured.RequestUri!.AbsolutePath);
        Assert.True(result.RecordingDeleted);
    }

    [Fact]
    public async Task StructuredErrorsKeepBackendCodeAndStatus()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { code = "SUBJECT_CODE_CONFLICT", message = "duplicate" }),
        })) { BaseAddress = new Uri("http://localhost/") };

        var error = await Assert.ThrowsAsync<ResearchProjectApiException>(() =>
            new HttpResearchProjectClient(http).CreateSubjectAsync("project-1", "S-1", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("SUBJECT_CODE_CONFLICT", error.Code);
        Assert.Equal("duplicate", error.Message);
    }

    [Fact]
    public async Task ProjectImportStreamsTheSelectedFileAndReportsValidatedFacts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edf");
        var fileBytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(path, fileBytes);
        var progress = new List<long>();
        byte[]? requestBody = null;
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            captured = request;
            requestBody = await request.Content!.ReadAsByteArrayAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    project_id = "project-1", session_id = "session-1", subject_id = "subject-1",
                    recording_id = "recording-1", original_name = Path.GetFileName(path), extension = ".edf",
                    created_at = "2026-11-24T00:00:00Z", sfreq = 512, duration_s = 8,
                    channels = new[] { "F3", "C3" }, channel_types = new[] { "eeg", "eeg" },
                    channel_units = new[] { "V", "V" }, source_sha256 = new string('a', 64), file_size_bytes = fileBytes.Length,
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        try
        {
            const string snapshot = "{\"Channels\":[]}";
            var result = await new HttpResearchProjectClient(http).ImportRecordingAsync("project-1", path, "subject-1", null,
                snapshot, new InlineProgress(progress.Add), CancellationToken.None);

            Assert.Equal("/api/projects/project-1/recordings/import", captured!.RequestUri!.AbsolutePath);
            Assert.Equal("multipart/form-data", captured.Content!.Headers.ContentType!.MediaType);
            Assert.Contains(Path.GetFileName(path), Encoding.UTF8.GetString(requestBody!));
            Assert.Contains("subject-1", Encoding.UTF8.GetString(requestBody!));
            Assert.Contains("channel_configuration_snapshot_json", Encoding.UTF8.GetString(requestBody!));
            Assert.True(requestBody.AsSpan().IndexOf(fileBytes) >= 0);
            Assert.Contains(4096L, progress);
            Assert.Equal(["F3", "C3"], result.Channels);
            Assert.Equal("session-1", result.SessionId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProjectImportUploadsOnlyBoundedChannelIdentityFromAdjacentSidecar()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edf");
        var sidecarPath = path + ".labels.json";
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await File.WriteAllTextAsync(sidecarPath, """
            {"exported_signal_sha256":"abc","hardware":{"channel_configuration_snapshot_json":{"Channels":[]}},
             "channels":[{"export_label":"CH0001","source_label":"Fz","native_channel_index":8,"kind":"2"}],
             "events":[{"large":"ignored"}]}
            """);
        string? requestBody = null;
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            requestBody = Encoding.UTF8.GetString(await request.Content!.ReadAsByteArrayAsync(token));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    project_id = "project-1", session_id = "session-1", subject_id = "subject-1",
                    recording_id = "recording-1", original_name = Path.GetFileName(path), extension = ".edf",
                    created_at = "2026-11-24T00:00:00Z", sfreq = 512, duration_s = 8,
                    channels = new[] { "Fz" }, channel_types = new[] { "eeg" },
                    channel_units = new[] { "V" }, source_sha256 = new string('a', 64), file_size_bytes = 3,
                }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        try
        {
            await new HttpResearchProjectClient(http).ImportRecordingAsync("project-1", path, "subject-1", null,
                null, null, CancellationToken.None);
            using var request = JsonDocument.Parse(ExtractMultipartField(requestBody!, "export_channel_identity_json"));
            Assert.Equal("abc", request.RootElement.GetProperty("exported_signal_sha256").GetString());
            Assert.True(request.RootElement.TryGetProperty("channel_configuration_snapshot_json", out _));
            Assert.False(request.RootElement.ToString().Contains("events", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
            File.Delete(sidecarPath);
        }
    }

    private static string ExtractMultipartField(string body, string field)
    {
        var start = body.IndexOf(field, StringComparison.Ordinal);
        Assert.True(start >= 0, $"multipart field {field} was not found: {body}");
        start = body.IndexOf("\r\n\r\n", start + field.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"multipart field {field} has no header terminator");
        start += 4;
        var end = body.IndexOf("\r\n--", start, StringComparison.Ordinal);
        Assert.True(end >= start, $"multipart field {field} was not terminated");
        return body[start..end];
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) :
            this((request, _) => Task.FromResult(respond(request))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => this.respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Projects.Api;

namespace BrainPlatform.Desktop.Tests.Projects;

public sealed class HttpResearchProjectClientTests
{
    [Fact]
    public async Task ImportAndRecordEditSendDemographicsUsingTheirCorrectOwners()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal(26, json.RootElement.GetProperty("age_at_recording_years").GetInt32());
                Assert.False(json.RootElement.TryGetProperty("sex", out _));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                { session_id = "s", project_id = "p", recording_id = "r", subject_id = "u", label = "record",
                    created_at = "", subject_sex = "female", age_at_recording_years = 26 }) };
            }
            var body = await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("new_subject_sex", body);
            Assert.Contains("female", body);
            Assert.Contains("age_at_recording_years", body);
            Assert.Contains("25", body);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new
            { project_id = "p", session_id = "s", subject_id = "u", recording_id = "r", original_name = "record.edf",
                extension = ".edf", created_at = "", sfreq = 512, duration_s = 1, channels = new[] { "F3" },
                channel_types = new[] { "eeg" }, channel_units = new[] { "V" }, source_sha256 = new string('a', 64), file_size_bytes = 3 }) };
        })) { BaseAddress = new Uri("http://localhost/") };
        try
        {
            var client = new HttpResearchProjectClient(http);
            await client.ImportRecordingAsync("p", path, null, null, null, null, CancellationToken.None, "姓名", "female", 25);
            var session = await client.BindSessionSubjectAsync("p", "s", new("record", "u", 26), CancellationToken.None);
            Assert.Equal(26, session.AgeAtRecordingYears);
            Assert.Equal("female", session.SubjectSex);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SubjectManagementUsesTypedProjectRoutesAndExplicitIdentityBinding()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add(request.Method + " " + path);
            if (path.EndsWith("/sessions/s"))
            {
                using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("subject_id").ValueKind);
                Assert.False(json.RootElement.TryGetProperty("subject_code", out _));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                { session_id = "s", project_id = "p", recording_id = "r", subject_id = (string?)null, label = "record", created_at = "" }) };
            }
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { subject_id = "u", detached_sessions = 2 }) };
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("姓名", body.RootElement.GetProperty("name").GetString());
            Assert.Equal("female", body.RootElement.GetProperty("sex").GetString());
            var subject = new { subject_id = "u", project_id = "p", local_code = (string?)null, name = "姓名", sex = "female", created_at = "" };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = path.EndsWith("/resolve") ? JsonContent.Create(new { subject }) : JsonContent.Create(subject) };
        })) { BaseAddress = new Uri("http://localhost/") };
        var client = new HttpResearchProjectClient(http);
        Assert.Equal("female", (await client.ResolveSubjectAsync("p", new(null, "姓名", "female"), CancellationToken.None)).Subject!.Sex);
        Assert.Equal("female", (await client.UpdateSubjectAsync("p", "u", new(null, "姓名", "female"), CancellationToken.None)).Sex);
        Assert.Equal(2, (await client.DeleteSubjectAsync("p", "u", CancellationToken.None)).DetachedSessions);
        Assert.Null((await client.BindSessionSubjectAsync("p", "s", new("record", null), CancellationToken.None)).SubjectId);
        Assert.Equal(new[] { "POST /api/projects/p/subjects/resolve", "PATCH /api/projects/p/subjects/u",
            "DELETE /api/projects/p/subjects/u", "PATCH /api/projects/p/sessions/s" }, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("姓名")]
    public async Task ImportSupportsNoSubjectOrANameWithoutCode(string? subjectName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.edf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            var body = Encoding.UTF8.GetString(await request.Content!.ReadAsByteArrayAsync(token));
            Assert.DoesNotContain("name=subject_id", body);
            Assert.DoesNotContain("name=new_subject_local_code", body);
            if (subjectName is null) Assert.DoesNotContain("new_subject_name", body);
            else Assert.Contains(subjectName, body);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new
            {
                project_id = "p", session_id = "s", subject_id = (string?)null, recording_id = "r",
                original_name = "record.edf", extension = ".edf", created_at = "", sfreq = 512, duration_s = 1,
                channels = new[] { "F3" }, channel_types = new[] { "eeg" }, channel_units = new[] { "V" },
                source_sha256 = new string('a', 64), file_size_bytes = 3
            }) };
        })) { BaseAddress = new Uri("http://localhost/") };
        try
        {
            var result = await new HttpResearchProjectClient(http).ImportRecordingAsync("p", path, null, null,
                null, null, CancellationToken.None, subjectName);
            Assert.Equal("r", result.RecordingId);
            Assert.Null(result.SubjectId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EditSessionSendsOptionalSubjectFieldsAndReadsNullableSubject()
    {
        string? body = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/projects/project-1/sessions/session-1", request.RequestUri!.AbsolutePath);
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                session_id = "session-1", project_id = "project-1", subject_id = (string?)null,
                recording_id = "recording-1", condition_id = (string?)null, label = "record", created_at = "",
                subject_code = (string?)null, subject_name = ""
            }) };
        })) { BaseAddress = new Uri("http://localhost/") };
        var result = await new HttpResearchProjectClient(http).EditSessionAsync("project-1", "session-1",
            new EditResearchSessionRequest("record", "", ""), CancellationToken.None);
        Assert.Null(result.SubjectId);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("", json.RootElement.GetProperty("subject_code").GetString());
        Assert.Equal("", json.RootElement.GetProperty("subject_name").GetString());
    }

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
    public async Task RenameSessionPatchesOnlyTheProjectScopedDisplayName()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/projects/project-1/sessions/session-1", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("复测", body.RootElement.GetProperty("label").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
            Assert.Single(request.Headers.GetValues("X-Request-ID"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { session_id = "session-1", project_id = "project-1",
                    subject_id = "subject-1", recording_id = "recording-1", condition_id = (string?)null,
                    label = "复测", created_at = "2026-11-25T00:00:00Z" }),
            };
        })) { BaseAddress = new Uri("http://localhost/") };

        var result = await new HttpResearchProjectClient(http).RenameSessionAsync(
            "project-1", "session-1", "复测", CancellationToken.None);

        Assert.Equal("复测", result.Label);
        Assert.Equal("recording-1", result.RecordingId);
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

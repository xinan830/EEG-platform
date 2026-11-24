using System.Net.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Projects.Api;

public sealed class HttpResearchProjectClient(HttpClient httpClient) : IResearchProjectClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<BackendResearchProject> EnsureProjectAsync(string projectId, string name, string description, CancellationToken cancellationToken) =>
        SendAsync<BackendResearchProject>(HttpMethod.Put, $"api/projects/desktop/{Escape(projectId)}",
            new EnsureDesktopProjectRequest(name, description), cancellationToken);

    public async Task<IReadOnlyList<BackendResearchSubject>> ListSubjectsAsync(string projectId, CancellationToken cancellationToken) =>
        await SendAsync<List<BackendResearchSubject>>(HttpMethod.Get, $"api/projects/{Escape(projectId)}/subjects", null, cancellationToken)
            .ConfigureAwait(false);

    public Task<BackendResearchSubject> CreateSubjectAsync(string projectId, string localCode, CancellationToken cancellationToken) =>
        SendAsync<BackendResearchSubject>(HttpMethod.Post, $"api/projects/{Escape(projectId)}/subjects",
            new CreateSubjectRequest(localCode), cancellationToken);

    public async Task<IReadOnlyList<BackendResearchSession>> ListSessionsAsync(string projectId, CancellationToken cancellationToken) =>
        await SendAsync<List<BackendResearchSession>>(HttpMethod.Get, $"api/projects/{Escape(projectId)}/sessions", null, cancellationToken)
            .ConfigureAwait(false);

    public Task<BackendResearchSession> CreateSessionAsync(string projectId, CreateResearchSessionRequest request, CancellationToken cancellationToken) =>
        SendAsync<BackendResearchSession>(HttpMethod.Post, $"api/projects/{Escape(projectId)}/sessions", request, cancellationToken);

    public async Task<BackendImportedRecording> ImportRecordingAsync(string projectId, string filePath, string? subjectId,
        string? newSubjectLocalCode, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new MultipartFormDataContent();
        var source = new ProgressStreamContent(file, file.Length, progress);
        source.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(source, "file", Path.GetFileName(filePath));
        if (subjectId is not null) content.Add(new StringContent(subjectId), "subject_id");
        if (newSubjectLocalCode is not null) content.Add(new StringContent(newSubjectLocalCode), "new_subject_local_code");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/projects/{Escape(projectId)}/recordings/import") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString("N"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<BackendImportedRecording>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new ResearchProjectApiException(response.StatusCode, "EMPTY_RESPONSE", "项目服务返回了空响应。");
    }

    public Task<BackendRecording> GetRecordingAsync(string recordingId, CancellationToken cancellationToken) =>
        SendAsync<BackendRecording>(HttpMethod.Get, $"api/recordings/{Escape(recordingId)}", null, cancellationToken);

    public Task<BackendRawSampleRange> ReadRawSamplesAsync(string recordingId, long startSample, long endSample,
        CancellationToken cancellationToken) =>
        SendAsync<BackendRawSampleRange>(HttpMethod.Get,
            $"api/recordings/{Escape(recordingId)}/raw-samples?start_sample={startSample}&end_sample={endSample}", null,
            cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString("N"));
        if (payload is not null) request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new ResearchProjectApiException(response.StatusCode, "EMPTY_RESPONSE", "项目服务返回了空响应。");
    }

    private static async Task<ResearchProjectApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
            HttpStatusCode.Conflict => "RESOURCE_CONFLICT",
            HttpStatusCode.UnprocessableEntity => "REQUEST_INVALID",
            _ => "PROJECT_API_ERROR",
        };
        var message = $"项目服务请求失败（HTTP {(int)response.StatusCode}）。";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("code", out var errorCode) && errorCode.ValueKind == JsonValueKind.String)
                    code = errorCode.GetString() ?? code;
                if (document.RootElement.TryGetProperty("message", out var errorMessage) && errorMessage.ValueKind == JsonValueKind.String)
                    message = errorMessage.GetString() ?? message;
            }
        }
        catch (JsonException) { }
        return new ResearchProjectApiException(response.StatusCode, code, message);
    }

    private static string Escape(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("资源 ID 不能为空。", nameof(value)) : Uri.EscapeDataString(value);
}

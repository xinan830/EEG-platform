using System.Net.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed class HttpAlgorithmClient : IAlgorithmClient, IRecordingRegistrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;

    public HttpAlgorithmClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<AlgorithmCatalogItem>> ListAlgorithmsAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<AlgorithmCatalogResponse>(
            HttpMethod.Get, "api/algorithms", null, cancellationToken);
        return response.Algorithms;
    }

    public Task<AnalysisRunResponse> CreateRunAsync(
        AnalysisRunRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AnalysisRunResponse>(HttpMethod.Post, "api/runs", request, cancellationToken);

    public Task<RegisteredRecording> RegisterWpfRecordingAsync(
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("记录目录不能为空。", nameof(sourceDirectory));
        }

        return SendAsync<RegisteredRecording>(
            HttpMethod.Post,
            "api/recordings/register-wpf",
            new WpfRecordingRegistrationRequest(sourceDirectory),
            cancellationToken);
    }

    public Task<AnalysisRunResponse> GetRunAsync(string runId, CancellationToken cancellationToken) =>
        SendAsync<AnalysisRunResponse>(
            HttpMethod.Get, $"api/runs/{Uri.EscapeDataString(RequireId(runId))}", null, cancellationToken);

    public async Task<IReadOnlyList<RunArtifact>> ListArtifactsAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        return await SendAsync<List<RunArtifact>>(
            HttpMethod.Get, $"api/runs/{Uri.EscapeDataString(RequireId(runId))}/artifacts", null, cancellationToken);
    }

    public Task<StructuredPreviewResponse> GetStructuredPreviewAsync(
        string runId,
        int maxCells,
        CancellationToken cancellationToken)
    {
        if (maxCells is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCells));
        }

        return SendAsync<StructuredPreviewResponse>(
            HttpMethod.Get,
            $"api/runs/{Uri.EscapeDataString(RequireId(runId))}/structured-preview?max_cells={maxCells}",
            null,
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new AlgorithmApiException(
            response.StatusCode, "EMPTY_RESPONSE", "算法服务返回了空响应。");
    }

    private static async Task<AlgorithmApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var code = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
            HttpStatusCode.UnprocessableEntity => "REQUEST_INVALID",
            HttpStatusCode.Conflict => "REQUEST_CONFLICT",
            _ => "ALGORITHM_API_ERROR",
        };
        var message = response.ReasonPhrase ?? "算法服务请求失败。";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("code", out var errorCode))
            {
                code = errorCode.GetString() ?? code;
            }
            if (document.RootElement.TryGetProperty("message", out var errorMessage))
            {
                message = errorMessage.GetString() ?? message;
            }
            else if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                message = detail.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // Preserve the stable HTTP-derived error when a proxy returns non-JSON.
        }

        return new AlgorithmApiException(response.StatusCode, code, message);
    }

    private static string RequireId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("资源 ID 不能为空。", nameof(value))
            : value;
}

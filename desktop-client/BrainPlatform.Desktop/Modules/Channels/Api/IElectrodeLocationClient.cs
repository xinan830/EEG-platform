using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Channels.Api;

public sealed record ElectrodeLocationResolution(
    string RequestedLabel,
    string Status,
    string? CanonicalSite,
    double[]? Coordinates,
    string? CoordinateFrame,
    string? CoordinateUnit,
    string SourceId,
    string SourceVersion);

public interface IElectrodeLocationClient
{
    Task<IReadOnlyList<ElectrodeLocationResolution>> ResolveAsync(
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken);
}

public sealed class HttpElectrodeLocationClient(HttpClient httpClient) : IElectrodeLocationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<IReadOnlyList<ElectrodeLocationResolution>> ResolveAsync(
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/electrode-locations/resolve",
            new ResolveRequest(labels),
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken);
            throw new HttpRequestException(error?.Message ?? $"标准电极位置解析失败（HTTP {(int)response.StatusCode}）。");
        }

        var result = await response.Content.ReadFromJsonAsync<ResolveResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("标准电极位置解析响应为空。");
        return result.Locations;
    }

    private sealed record ResolveRequest(IReadOnlyList<string> Labels);
    private sealed record ResolveResponse(IReadOnlyList<ElectrodeLocationResolution> Locations);
    private sealed record ApiError(string Code, string Message);
}

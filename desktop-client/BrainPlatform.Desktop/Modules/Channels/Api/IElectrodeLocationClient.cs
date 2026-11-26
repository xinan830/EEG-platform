using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Channels.Api;

public sealed record ElectrodeLocationResolution(
    string RequestedLabel,
    string Status,
    string? CanonicalSite,
    double[]? Coordinates,
    double[]? MapCoordinates,
    string? CoordinateFrame,
    string? CoordinateUnit,
    string SourceId,
    string SourceVersion);

public interface IElectrodeLocationClient
{
    Task<IReadOnlyList<ElectrodeLocationResolution>> ResolveAsync(
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken);

    Task<ElectrodeScalpModel3D> GetScalpModel3DAsync(CancellationToken cancellationToken);

    Task<string> GetCortexModel3DJsonAsync(CancellationToken cancellationToken) =>
        Task.FromException<string>(new NotSupportedException("三维大脑皮层资产未配置。"));

    Task<ElectrodeMontageCatalog> GetMontageCatalogAsync(CancellationToken cancellationToken) =>
        Task.FromException<ElectrodeMontageCatalog>(new NotSupportedException("电极模板目录服务未配置。"));

    Task<ElectrodeMontagePreview> GetMontagePreviewAsync(string montageName, CancellationToken cancellationToken) =>
        Task.FromException<ElectrodeMontagePreview>(new NotSupportedException("电极模板预览服务未配置。"));
}

public sealed class HttpElectrodeLocationClient(HttpClient httpClient) : IElectrodeLocationClient
{
    private readonly object modelGate = new();
    private Task<ElectrodeScalpModel3D>? scalpModel;
    private Task<string>? cortexModelJson;

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

    public Task<ElectrodeScalpModel3D> GetScalpModel3DAsync(CancellationToken cancellationToken)
    {
        Task<ElectrodeScalpModel3D> request;
        lock (modelGate)
        {
            if (scalpModel is null || scalpModel.IsFaulted || scalpModel.IsCanceled)
                scalpModel = LoadScalpModel3DAsync();
            request = scalpModel;
        }
        return request.WaitAsync(cancellationToken);
    }

    private async Task<ElectrodeScalpModel3D> LoadScalpModel3DAsync()
    {
        using var response = await httpClient.GetAsync("api/topomap/3d/model", CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, CancellationToken.None);
            throw new HttpRequestException(error?.Message ?? $"三维头模读取失败（HTTP {(int)response.StatusCode}）。");
        }

        return await response.Content.ReadFromJsonAsync<ElectrodeScalpModel3D>(JsonOptions, CancellationToken.None)
            ?? throw new JsonException("三维头模响应为空。");
    }

    public Task<string> GetCortexModel3DJsonAsync(CancellationToken cancellationToken)
    {
        Task<string> request;
        lock (modelGate)
        {
            if (cortexModelJson is null || cortexModelJson.IsFaulted || cortexModelJson.IsCanceled)
                cortexModelJson = LoadCortexModel3DJsonAsync();
            request = cortexModelJson;
        }
        return request.WaitAsync(cancellationToken);
    }

    private async Task<string> LoadCortexModel3DJsonAsync()
    {
        using var response = await httpClient.GetAsync("api/topomap/3d/cortex", CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, CancellationToken.None);
            throw new HttpRequestException(error?.Message ?? $"三维大脑皮层读取失败（HTTP {(int)response.StatusCode}）。");
        }

        return await response.Content.ReadAsStringAsync(CancellationToken.None);
    }

    public async Task<ElectrodeMontageCatalog> GetMontageCatalogAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/topomap/3d/montages", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken);
            throw new HttpRequestException(error?.Message ?? $"电极模板目录读取失败（HTTP {(int)response.StatusCode}）。");
        }

        return await response.Content.ReadFromJsonAsync<ElectrodeMontageCatalog>(JsonOptions, cancellationToken)
            ?? throw new JsonException("电极模板目录响应为空。");
    }

    public async Task<ElectrodeMontagePreview> GetMontagePreviewAsync(string montageName, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(montageName);
        using var response = await httpClient.GetAsync($"api/topomap/3d/montages/{encoded}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken);
            throw new HttpRequestException(error?.Message ?? $"电极模板坐标读取失败（HTTP {(int)response.StatusCode}）。");
        }

        return await response.Content.ReadFromJsonAsync<ElectrodeMontagePreview>(JsonOptions, cancellationToken)
            ?? throw new JsonException("电极模板坐标响应为空。");
    }

    private sealed record ResolveRequest(IReadOnlyList<string> Labels);
    private sealed record ResolveResponse(IReadOnlyList<ElectrodeLocationResolution> Locations);
    private sealed record ApiError(string Code, string Message);
}

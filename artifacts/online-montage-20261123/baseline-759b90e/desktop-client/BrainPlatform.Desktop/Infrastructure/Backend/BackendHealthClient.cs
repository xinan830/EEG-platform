using System.Net.Http;
using BrainPlatform.Desktop.Infrastructure.Backend;

namespace BrainPlatform.Desktop.Infrastructure.Backend;

public sealed class BackendHealthClient(HttpClient httpClient) : IBackendHealthClient
{
    public Uri Endpoint => new(
        httpClient.BaseAddress ?? throw new InvalidOperationException("缺少后端地址"),
        "api/health");

    public async Task<BackendConnectionState> CheckAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.Now;

        try
        {
            using var response = await httpClient.GetAsync("api/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return BackendConnectionState.Unavailable(
                    $"后端返回 HTTP {(int)response.StatusCode}",
                    checkedAt);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return BackendHealthResponseParser.Parse(payload, checkedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BackendConnectionState.Unavailable("本地科学引擎连接超时", checkedAt);
        }
        catch (HttpRequestException)
        {
            return BackendConnectionState.Unavailable("本地科学引擎未启动或不可访问", checkedAt);
        }
    }
}

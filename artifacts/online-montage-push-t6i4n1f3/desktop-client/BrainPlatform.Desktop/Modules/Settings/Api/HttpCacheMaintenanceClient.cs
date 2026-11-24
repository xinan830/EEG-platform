using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Settings.Services;

namespace BrainPlatform.Desktop.Modules.Settings.Api;

public sealed class HttpCacheMaintenanceClient(HttpClient httpClient) : ICacheMaintenanceClient
{
    public async Task<BackendCacheClearResult> ClearAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/maintenance/cache/clear")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Request-ID", requestId);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        DesktopLog.Info($"cache_clear_http request_id={requestId} status={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            var message = $"缓存服务返回 HTTP {(int)response.StatusCode}，请稍后重试。";
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                if (error.RootElement.ValueKind == JsonValueKind.Object &&
                    error.RootElement.TryGetProperty("message", out var detail) && detail.ValueKind == JsonValueKind.String)
                    message = detail.GetString() ?? message;
            }
            catch (JsonException) { /* A non-JSON proxy error retains the HTTP diagnostic. */ }
            throw new InvalidOperationException($"{message}（请求 {requestId}）");
        }
        var result = await response.Content.ReadFromJsonAsync<BackendCacheClearResult>(cancellationToken).ConfigureAwait(false);
        if (result is null || result.Status != "cleared" || result.DeletedWindows < 0 ||
            result.DeletedSessions < 0 || result.DeletedAttempts < 0 || result.DeletedSubscriptions < 0 ||
            result.DeletedFiles < 0 || result.FreedBytes < 0)
            throw new InvalidOperationException("缓存服务返回了无效的清理结果。");
        return result;
    }
}

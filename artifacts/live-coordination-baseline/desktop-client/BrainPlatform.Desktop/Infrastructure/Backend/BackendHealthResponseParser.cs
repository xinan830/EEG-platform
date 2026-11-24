using System.Text.Json;
using BrainPlatform.Desktop.Infrastructure.Backend;

namespace BrainPlatform.Desktop.Infrastructure.Backend;

public static class BackendHealthResponseParser
{
    public static BackendConnectionState Parse(string payload, DateTimeOffset checkedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            var service = root.TryGetProperty("service", out var serviceElement)
                ? serviceElement.GetString()
                : null;

            return string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)
                ? BackendConnectionState.Available(service ?? string.Empty, checkedAt)
                : BackendConnectionState.Unavailable("后端健康响应不符合预期", checkedAt);
        }
        catch (JsonException)
        {
            return BackendConnectionState.Unavailable("后端返回了无效的健康响应", checkedAt);
        }
    }
}

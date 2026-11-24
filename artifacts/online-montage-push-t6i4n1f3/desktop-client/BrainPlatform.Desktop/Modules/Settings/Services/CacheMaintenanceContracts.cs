using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Settings.Services;

public sealed record BackendCacheClearResult(
    [property: JsonPropertyName("status"), JsonRequired] string Status,
    [property: JsonPropertyName("deleted_sessions"), JsonRequired] int DeletedSessions,
    [property: JsonPropertyName("deleted_windows"), JsonRequired] int DeletedWindows,
    [property: JsonPropertyName("deleted_attempts"), JsonRequired] int DeletedAttempts,
    [property: JsonPropertyName("deleted_subscriptions"), JsonRequired] int DeletedSubscriptions,
    [property: JsonPropertyName("deleted_files"), JsonRequired] int DeletedFiles,
    [property: JsonPropertyName("freed_bytes"), JsonRequired] long FreedBytes);

public sealed record CacheClearResult(int DeletedWindows, int DeletedFiles, long FreedBytes);

public interface ICacheMaintenanceClient
{
    Task<BackendCacheClearResult> ClearAsync(CancellationToken cancellationToken);
}

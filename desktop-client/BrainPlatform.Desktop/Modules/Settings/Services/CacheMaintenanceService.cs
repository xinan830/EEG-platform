using System.IO;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Review.Cache;

namespace BrainPlatform.Desktop.Modules.Settings.Services;

/// <summary>Coordinates the backend reset and local derived files under recording-access exclusion.</summary>
public sealed class CacheMaintenanceService(
    ICacheMaintenanceClient backend, ReviewCacheCleaner local, RecordingSessionManager recordings)
{
    public Task<CacheClearResult> ClearAsync(CancellationToken cancellationToken = default) =>
        recordings.RunMaintenanceAsync(async () =>
        {
            var result = await backend.ClearAsync(cancellationToken).ConfigureAwait(false);
            LocalCacheClearResult localResult;
            try { localResult = await local.ClearAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException("后端缓存及索引已清除，本地回放缓存未清完；请关闭占用文件的程序后重试。", exception);
            }
            var total = new CacheClearResult(result.DeletedWindows,
                result.DeletedFiles + localResult.DeletedFiles, result.FreedBytes + localResult.FreedBytes);
            DesktopLog.Info($"cache_cleared deleted_windows={total.DeletedWindows} deleted_files={total.DeletedFiles} freed_bytes={total.FreedBytes}");
            return total;
        }, cancellationToken);
}

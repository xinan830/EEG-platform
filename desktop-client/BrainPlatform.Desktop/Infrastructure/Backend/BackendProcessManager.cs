using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace BrainPlatform.Desktop.Infrastructure.Backend;

/// <summary>
/// 负责在 WPF 启动时拉起后端进程，并在退出时关闭它。
/// 打包发布时后端可执行文件位于 &lt;AppDir&gt;\backend\brain_backend.exe。
/// 开发模式下（后端可执行不存在）不做任何操作，假设后端已手动启动。
/// </summary>
public sealed class BackendProcessManager : IDisposable
{
    private const string BackendRelativePath = @"backend\brain_backend.exe";
    private const string BackendUrl = "http://127.0.0.1:8000/api/health";
    private const int MaxWaitMs = 20_000;
    private const int PollIntervalMs = 300;

    private Process? _process;
    private bool _disposed;
    public bool OwnsBackendProcess => _process is not null;

    /// <summary>
    /// 如果打包后的后端可执行文件存在，启动它并等待就绪。
    /// </summary>
    /// <returns>true = 后端已就绪；false = 跳过（开发模式或启动失败）</returns>
    public async Task<bool> StartAndWaitAsync(CancellationToken cancellationToken = default)
    {
        var exePath = GetBackendExePath();
        if (exePath is null)
        {
            // 开发模式：不存在打包后端，期望开发者手动启动
            return false;
        }

        // Reuse an already running local backend instead of starting a second
        // server on port 8000 or terminating a development process on exit.
        if (await IsHealthyAsync(cancellationToken))
            return true;

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动后端进程");

        return await WaitForHealthyAsync(cancellationToken);
    }

    /// <summary>
    /// 轮询 /api/health 直到后端就绪或超时。
    /// </summary>
    private static async Task<bool> WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(MaxWaitMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var resp = await http.GetAsync(BackendUrl, cancellationToken);
                if (resp.IsSuccessStatusCode)
                    return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // 后端还没起来，继续等
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
        try
        {
            using var response = await http.GetAsync(BackendUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 返回打包后后端可执行文件的完整路径；
    /// 若文件不存在（开发模式）则返回 null。
    /// </summary>
    private static string? GetBackendExePath()
    {
        var appDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(appDir, BackendRelativePath);
        return File.Exists(candidate) ? candidate : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // 进程可能已经退出，忽略
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }
}

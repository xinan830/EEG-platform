using System.Net;
using System.Net.Http;
using System.Text;
using BrainPlatform.Desktop.Modules.Settings.Api;
using BrainPlatform.Desktop.Modules.Settings.Services;
using BrainPlatform.Desktop.Modules.Settings.ViewModels;

namespace BrainPlatform.Desktop.Tests.Settings;

public sealed class CacheMaintenanceTests
{
    [Fact]
    public async Task ClearsOnlyFixedReviewRootsAndReportsActualBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-cache-clean-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "review-cache", "nested"));
            Directory.CreateDirectory(Path.Combine(root, "review-display-cache"));
            Directory.CreateDirectory(Path.Combine(root, "recordings"));
            await File.WriteAllBytesAsync(Path.Combine(root, "review-cache", "nested", "chunk.bin"), new byte[23]);
            await File.WriteAllBytesAsync(Path.Combine(root, "review-display-cache", "chunk.bin"), new byte[17]);
            var raw = Path.Combine(root, "recordings", "samples.bin");
            await File.WriteAllTextAsync(raw, "original recording");
            var cleaner = new ReviewCacheCleaner(root);
            var cleared = await cleaner.ClearAsync();
            Assert.Equal(new LocalCacheClearResult(2, 40), cleared);
            Assert.Equal("original recording", await File.ReadAllTextAsync(raw));
            Assert.False(Directory.Exists(Path.Combine(root, "review-cache")));
            Assert.Equal(new LocalCacheClearResult(0, 0), await cleaner.ClearAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LockedLocalFileDoesNotReportSuccessfulClear()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-cache-clean-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "review-cache"));
        try
        {
            var path = Path.Combine(root, "review-cache", "chunk.bin");
            await File.WriteAllBytesAsync(path, new byte[10]);
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            await Assert.ThrowsAsync<IOException>(() => new ReviewCacheCleaner(root).ClearAsync());
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task BusyBackendLeavesLocalFilesUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-cache-clean-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "review-cache"));
        try
        {
            var path = Path.Combine(root, "review-cache", "chunk.bin");
            await File.WriteAllTextAsync(path, "cached");
            await using var sessions = new BrainPlatform.Desktop.Modules.Recordings.Access.RecordingSessionManager();
            var service = new CacheMaintenanceService(new BusyClient(), new ReviewCacheCleaner(root), sessions);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAsync());
            Assert.Equal("cached", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LinkedDirectoryRefusesBothLocalTreesBeforeDeletingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-cache-clean-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "review-cache"));
        Directory.CreateDirectory(Path.Combine(root, "recordings"));
        var linked = Path.Combine(root, "review-display-cache");
        try
        {
            var cached = Path.Combine(root, "review-cache", "chunk.bin");
            var raw = Path.Combine(root, "recordings", "samples.bin");
            await File.WriteAllTextAsync(cached, "cached");
            await File.WriteAllTextAsync(raw, "original recording");
            Directory.CreateSymbolicLink(linked, Path.Combine(root, "recordings"));
            await Assert.ThrowsAsync<IOException>(() => new ReviewCacheCleaner(root).ClearAsync());
            Assert.Equal("cached", await File.ReadAllTextAsync(cached));
            Assert.Equal("original recording", await File.ReadAllTextAsync(raw));
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewModelPreventsDuplicateRequestsAndRestoresStateOnFailure()
    {
        var pending = new TaskCompletionSource<CacheClearResult>();
        var calls = 0;
        var viewModel = new CacheMaintenanceViewModel(() => { calls++; return pending.Task; });
        var first = viewModel.ClearAsync();
        Assert.True(viewModel.IsClearing);
        Assert.Equal("正在清理…", viewModel.ButtonText);
        await viewModel.ClearAsync();
        Assert.Equal(1, calls);
        pending.SetException(new InvalidOperationException("后台任务正在运行"));
        await first;
        Assert.False(viewModel.IsClearing);
        Assert.Contains("后台任务正在运行", viewModel.StatusText);
        Assert.DoesNotContain("已清除", viewModel.StatusText);
    }

    [Fact]
    public async Task ViewModelDisplaysWindowCountAndFreedBytes()
    {
        var viewModel = new CacheMaintenanceViewModel(() => Task.FromResult(new CacheClearResult(339, 678, 1024 * 1024)));
        await viewModel.ClearAsync();
        Assert.Contains("339", viewModel.StatusText);
        Assert.Contains("678", viewModel.StatusText);
        Assert.Contains("1.00 MB", viewModel.StatusText);
        Assert.False(viewModel.IsClearing);
    }

    [Fact]
    public async Task HttpClientUsesConnectedBackendAndTypedCleanupContract()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:9876/api/maintenance/cache/clear", request.RequestUri!.AbsoluteUri);
            Assert.Equal("{}", await request.Content!.ReadAsStringAsync());
            Assert.True(request.Headers.Contains("X-Request-ID"));
            return Json(HttpStatusCode.OK, """
                {"status":"cleared","deleted_sessions":1,"deleted_windows":2,"deleted_attempts":2,
                 "deleted_subscriptions":3,"deleted_files":4,"freed_bytes":512}
                """);
        })) { BaseAddress = new Uri("http://localhost:9876/") };
        var result = await new HttpCacheMaintenanceClient(http).ClearAsync(CancellationToken.None);
        Assert.Equal(2, result.DeletedWindows);
        Assert.Equal(512, result.FreedBytes);
    }

    [Fact]
    public async Task HttpClientShowsStructuredBusyMessage()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.Conflict,
            """{"code":"CACHE_BUSY","message":"请等待分析结束","request_id":"server-id"}"""))))
            { BaseAddress = new Uri("http://localhost/") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HttpCacheMaintenanceClient(http).ClearAsync(CancellationToken.None));
        Assert.Contains("请等待分析结束", error.Message);
    }

    [Fact]
    public async Task HttpClientRejectsIncompleteSuccessBeforeLocalDeletion()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"status":"cleared"}""")))) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new HttpCacheMaintenanceClient(http).ClearAsync(CancellationToken.None));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
    private sealed class BusyClient : ICacheMaintenanceClient
    {
        public Task<BackendCacheClearResult> ClearAsync(CancellationToken token) =>
            throw new InvalidOperationException("CACHE_BUSY");
    }
}

using System.Net;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpAlgorithmClientDispatcherTests
{
    [Fact]
    public async Task DynamicTopomapRequestDoesNotCaptureCallerSynchronizationContext()
    {
        var responseSource = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new PendingHandler(responseSource)) { BaseAddress = new Uri("http://localhost/") };
        var synchronizationContext = new CountingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        Task<TopomapResponse> request;
        try
        {
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            request = new HttpAlgorithmClient(client).GetDynamicTopomapAsync(
                "window-1", null, CancellationToken.None, ["rbp.relative.alpha"]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        responseSource.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                run_id = "run-1", recording_id = "recording-1", window_start_s = 0d, window_end_s = 5d,
                status = "complete", algorithm_version = "official-rbp-v4", implementation_version = "build",
                coordinate_source = "montage_position_snapshot", coordinate_frame = "mri", coordinate_unit = "m",
                fields = Array.Empty<object>(),
            }),
        });

        var result = await request;

        Assert.Equal("run-1", result.RunId);
        Assert.Equal(0, synchronizationContext.PostCount);
    }

    private sealed class PendingHandler(TaskCompletionSource<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => response.Task;
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int postCount;
        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref postCount);
            callback(state);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpResultScalarsTests
{
    [Fact]
    public async Task Scalar_request_uses_only_scalar_resource_and_does_not_capture_dispatcher()
    {
        var source = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PendingHandler(source);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var context = new CountingContext();
        var previous = SynchronizationContext.Current;
        Task<ResultScalarsResponse> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = new HttpAlgorithmClient(client).GetDynamicScalarValuesAsync(
                "window-1", "project-1", CancellationToken.None, ["psd.band-mean-density.alpha"]);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        source.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                window_id = "window-1", recording_id = "recording", algorithm_id = "psd",
                algorithm_version = "psd-v1", implementation_version = "build",
                window_start_s = 10, window_end_s = 20, status = "ready",
                fields = new[] { new { field_id = "psd.band-mean-density.alpha", label = "Alpha", unit = "V^2/Hz",
                    status = "ready", points = new[] { new { channel = "F3", value = 2e-12,
                        quality_status = "clean", quality_reasons = Array.Empty<string>() } } } },
            }),
        });

        var result = await pending;
        Assert.Equal("/api/analysis-windows/window-1/scalar-values?field_ids=psd.band-mean-density.alpha&project_id=project-1",
            handler.RequestUri!.PathAndQuery);
        Assert.Equal(2e-12, Assert.Single(Assert.Single(result.Fields).Points).Value);
        Assert.Equal(0, context.PostCount);
    }

    private sealed class PendingHandler(TaskCompletionSource<HttpResponseMessage> source) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { RequestUri = request.RequestUri; return source.Task; }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int PostCount { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state) => PostCount++;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkWorkbenchClientTests
{
    [Fact]
    public async Task StateTimelineKeepsBackendDurationsAndUnits()
    {
        using var http=new HttpClient(new Handler(request =>
        {
            Assert.Equal("/api/runs/run/tnk-workbench",request.RequestUri!.AbsolutePath);
            Assert.Equal("?cursor_s=149.982",request.RequestUri.Query);
            return Json(new {run_id="run",recording_id="recording",valid_duration_s=12.5,
                states=Array.Empty<object>(),timeline=new[] {new {cursor_s=14.0,valid_duration_s=12.5,
                states=new[] {new {label="平稳专注",color="#42A5F5",duration_s=12.5,percent=100.0}}}}});
        })) {BaseAddress=new Uri("http://localhost/")};
        var result=await new HttpAlgorithmClient(http).GetTnkWorkbenchSummaryAsync("run",149.982,CancellationToken.None);
        Assert.Equal(14,Assert.Single(result.Timeline).CursorSeconds);
        Assert.Equal(12.5,Assert.Single(result.Timeline[0].States).DurationSeconds);
    }
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK) {Content=JsonContent.Create(value)};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));
    }
}

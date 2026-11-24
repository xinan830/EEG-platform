using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Channels;

namespace BrainPlatform.Desktop.Tests.Review;

/// <summary>Exercises the HTTP adapter without a Python server or any scientific computation.</summary>
internal sealed class ControlledReviewHttpHandler : HttpMessageHandler
{
    private readonly Channel<PendingResponse> pending = Channel.CreateUnbounded<PendingResponse>();
    public bool HoldNextWarmup { get; set; }
    public bool HoldNextBatch { get; set; }
    public async Task<PendingResponse> NextAsync() =>
        await pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/contract"))
            return Json("{\"filter_contract\":{\"algorithm_version\":\"fixture-v1\",\"checkpoint_version\":\"fixture-v1\"}}");
        if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
        if (path.EndsWith("/checkpoint") && request.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
        if (path.EndsWith("/checkpoint")) return Checkpoint();
        var warmup = path.EndsWith("/warmup/binary");
        var batch = path.EndsWith("/batches/binary");
        byte[]? payload = batch ? await request.Content!.ReadAsByteArrayAsync(token) : null;
        if ((warmup && HoldNextWarmup) || (batch && HoldNextBatch))
        {
            if (warmup) HoldNextWarmup = false; else HoldNextBatch = false;
            var response = new PendingResponse();
            pending.Writer.TryWrite(response);
            if (await response.Fails.Task) return new(HttpStatusCode.InternalServerError);
        }
        if (warmup) return Checkpoint();
        if (batch) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload!) };
        if (path.EndsWith("/sessions")) return new(HttpStatusCode.Created);
        throw new InvalidOperationException($"Unexpected fixture HTTP path: {path}");
    }

    private static HttpResponseMessage Checkpoint() => Json("{\"checkpoint_b64\":\"fixture\",\"version\":\"fixture-v1\"}");
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    internal sealed class PendingResponse
    {
        public TaskCompletionSource<bool> Fails { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(bool fails = false) => Fails.TrySetResult(fails);
    }
}

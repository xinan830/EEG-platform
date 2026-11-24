using System.Diagnostics;
using System.Net.Http;

namespace BrainPlatform.Desktop.Infrastructure.Diagnostics;

/// <summary>Correlates local HTTP calls without logging their bodies.</summary>
public sealed class DiagnosticHttpMessageHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestId = request.Headers.TryGetValues("X-Request-ID", out var existing)
            ? existing.SingleOrDefault() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
        request.Headers.Remove("X-Request-ID");
        request.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            DesktopLog.Info($"http_completed request_id={requestId} method={request.Method.Method} path={request.RequestUri?.AbsolutePath} status={(int)response.StatusCode} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:0.0}");
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            DesktopLog.Error($"http_failed request_id={requestId} method={request.Method.Method} path={request.RequestUri?.AbsolutePath} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:0.0}", exception);
            throw;
        }
    }
}

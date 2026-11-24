using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient
{
    private static string DiagnosticIdentity(object? payload) => payload switch
    {
        DynamicWindowRequest window => $" analysis_request_id={window.RequestId ?? "none"}",
        CustomFrequencyWindowPairRequest pair => $" psd_window_id={pair.PsdWindowId} rbp_window_id={pair.RbpWindowId}",
        CustomFrequencyRangeRequest range => $" psd_window_id={range.PsdWindowId} rbp_window_id={range.RbpWindowId}",
        _ => string.Empty,
    };

    private static async Task<T?> ReadMeasuredResponseAsync<T>(HttpResponseMessage response,
        string requestId, string path, string identity, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        using var measured = new ResponseReadDiagnosticsStream(
            await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        using var content = new StreamContent(measured);
        content.Headers.ContentType = response.Content.Headers.ContentType;
        var outcome = "failed";
        try
        {
            var result = await content.ReadFromJsonAsync<T>(JsonOptions, token).ConfigureAwait(false);
            outcome = "ok";
            return result;
        }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            DesktopLog.Info($"http_response_body_perf request_id={requestId} path={path}{identity}" +
                $" outcome={outcome} response_bytes={measured.BytesRead} read_calls={measured.ReadCalls}" +
                $" body_read_ms={measured.ReadMilliseconds:0.000} body_json_ms={elapsed:0.000}" +
                $" json_nonread_ms={Math.Max(0, elapsed - measured.ReadMilliseconds):0.000}");
        }
    }
}

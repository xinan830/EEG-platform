using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Export;

namespace BrainPlatform.Desktop.Modules.Algorithms.Api;

public sealed partial class HttpAlgorithmClient : IRecordingSignalExportClient
{
    public async Task DownloadRawSignalExportAsync(string sourceDirectory, string format, Stream destination,
        CancellationToken cancellationToken)
    {
        if (format is not ("edf" or "bdf")) throw new ArgumentOutOfRangeException(nameof(format));
        // Long exports use their own cancellable transport, independent of algorithm request timeouts.
        using var transport = new HttpClient(new DiagnosticHttpMessageHandler(new HttpClientHandler()))
        {
            BaseAddress = httpClient.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var client = new HttpAlgorithmClient(transport);
        await client.DownloadRegisteredSignalAsync(sourceDirectory, format, destination, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task DownloadRegisteredSignalAsync(string sourceDirectory, string format, Stream destination,
        CancellationToken cancellationToken)
    {
        var recording = await SendAsync<RegisteredRecording>(HttpMethod.Post, "api/recordings/register-wpf",
            new WpfRecordingRegistrationRequest(sourceDirectory, SignalMode: "raw", AnalysisMontageProfiles: []),
            cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"api/recordings/{Uri.EscapeDataString(RequireId(recording.Id))}/signal-export")
        {
            Content = JsonContent.Create(new { format }),
        };
        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString("N"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "application/zip")
            throw new InvalidDataException("导出服务未返回预期的录音数据包。");
        await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}

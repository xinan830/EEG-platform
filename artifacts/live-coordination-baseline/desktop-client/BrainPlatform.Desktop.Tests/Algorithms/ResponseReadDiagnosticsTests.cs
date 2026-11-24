using System.IO;
using System.Net;
using System.Text;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class ResponseReadDiagnosticsTests
{
    [Fact]
    public async Task Streaming_counts_only_consumed_bytes_and_preserves_cancellation_and_ownership()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        using (var measured = new ResponseReadDiagnosticsStream(source))
        {
            Assert.Equal(0, source.Position);
            var buffer = new byte[2];
            Assert.Equal(2, measured.Read(buffer, 0, 2));
            Assert.Equal(2, await measured.ReadAsync(buffer.AsMemory()));
            Assert.Equal(0, await measured.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await measured.ReadAsync(buffer.AsMemory(), cancelled.Token));
            Assert.Equal(4, measured.BytesRead);
            Assert.Equal(4, measured.ReadCalls);
            Assert.True(measured.ReadMilliseconds >= 0);
            Assert.Throws<NotSupportedException>(() => measured.Write(buffer, 0, 1));
        }
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Measured_http_path_preserves_json_charset_and_null_metrics()
    {
        var previous = Environment.GetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS");
        Environment.SetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS", "1");
        try
        {
            using var http = new HttpClient(new UnicodeResponse()) { BaseAddress = new Uri("http://localhost/") };
            var value = await new HttpAlgorithmClient(http).GetCustomFrequencyBinsAsync(
                new CustomFrequencyWindowPairRequest("test-psd", "test-rbp"), CancellationToken.None);
            var row = Assert.Single(value.Channels);
            Assert.Equal("测试通道", row.Channel);
            Assert.Null(Assert.Single(row.PsdDensityUv2PerHz));
        }
        finally { Environment.SetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS", previous); }
    }

    private sealed class UnicodeResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"mode\":\"bins\",\"channels\":[{\"channel\":\"测试通道\",\"psd_density_uv2_per_hz\":[null]}]}",
                    Encoding.Unicode, "application/json"),
            });
    }
}

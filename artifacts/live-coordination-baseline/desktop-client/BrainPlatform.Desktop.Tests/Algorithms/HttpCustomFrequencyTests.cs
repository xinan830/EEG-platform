using System.Net;
using System.Text;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class HttpCustomFrequencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_posts_typed_window_identity_and_bounds_and_keeps_null_values(bool range)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new HttpAlgorithmClient(http);
        if (range)
        {
            var result = await client.GetCustomFrequencyRangeAsync(
                new CustomFrequencyRangeRequest("psd-1", "rbp-1", 1.1, 3.4, "project"), CancellationToken.None);
            Assert.Null(Assert.Single(result.Channels).PsdDensityUv2PerHz);
            Assert.Equal(3.4, result.HighHz);
        }
        else
        {
            var result = await client.GetCustomFrequencyBinsAsync(
                new CustomFrequencyWindowPairRequest("psd-1", "rbp-1", "project"), CancellationToken.None);
            Assert.Null(Assert.Single(Assert.Single(result.Channels).PsdDensityUv2PerHz));
            Assert.Equal(1.125, Assert.Single(result.BinIntervals).HighHz);
        }
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/analysis-windows/custom-frequency/{(range ? "range" : "bins")}", handler.Path);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("psd-1", request.RootElement.GetProperty("psd_window_id").GetString());
        Assert.Equal("rbp-1", request.RootElement.GetProperty("rbp_window_id").GetString());
        Assert.Equal("project", request.RootElement.GetProperty("project_id").GetString());
        if (range) Assert.Equal(1.1, request.RootElement.GetProperty("low_hz").GetDouble());
    }

    [Fact]
    public async Task Projection_propagates_cancellation()
    {
        using var http = new HttpClient(new Handler { WaitForCancellation = true })
            { BaseAddress = new Uri("http://localhost/") };
        using var cancellation = new CancellationTokenSource();
        var pending = new HttpAlgorithmClient(http).GetCustomFrequencyBinsAsync(
            new CustomFrequencyWindowPairRequest("psd", "rbp"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool WaitForCancellation { get; init; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(token);
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
            var range = Path.EndsWith("/range", StringComparison.Ordinal);
            var body = range
                ? "{\"mode\":\"range\",\"low_hz\":1.1,\"high_hz\":3.4,\"channels\":[{\"channel\":\"Fz\",\"psd_density_uv2_per_hz\":null}]}"
                : "{\"mode\":\"bins\",\"frequency_hz\":[1],\"bin_intervals\":[{\"low_hz\":1,\"high_hz\":1.125}],\"channels\":[{\"channel\":\"Fz\",\"psd_density_uv2_per_hz\":[null]}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

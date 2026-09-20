using System.Net;
using System.Net.Http.Json;
using BrainPlatform.Desktop.Acquisition.Analysis;
using BrainPlatform.Desktop.Acquisition.Contracts;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class HttpLiveFilterBridgeTests
{
    [Fact]
    public async Task FilterChange_KeepsActiveFilterPublishingWhileNewSessionWarmsUp()
    {
        var handler = new ControllableLiveFilterHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var bridge = new HttpLiveFilterBridge(client);
        var displayedCounters = new List<long>();
        LiveDisplayFilterActivated? activation = null;
        bridge.FilteredBatchAvailable += (_, filtered) => displayedCounters.Add(filtered.Batch.LastSampleCounter);
        bridge.FilterConfigurationActivated += (_, value) => activation = value;

        bridge.Configure(new LiveDisplayFilterSettings(1, 30, null));
        await bridge.PublishAsync(AnalysisBatch(0), CancellationToken.None);
        bridge.ScheduleChange(
            new LiveDisplayFilterSettings(1, 40, null),
            10,
            [new AcquisitionBatch(0, 10, 2, Values(0), DateTimeOffset.UtcNow)],
            10);

        await bridge.PublishAsync(AnalysisBatch(10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        await handler.WarmupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([9L, 19L], displayedCounters);
        Assert.Null(activation);

        handler.ReleaseWarmup.TrySetResult();
        for (var firstCounter = 20L; firstCounter < 100 && activation is null; firstCounter += 10)
        {
            await Task.Delay(10);
            await bridge.PublishAsync(AnalysisBatch(firstCounter), CancellationToken.None);
        }

        Assert.NotNull(activation);
        Assert.True(activation.EffectiveFromRawSampleCounter >= 20);
        Assert.Equal(Enumerable.Range(0, displayedCounters.Count).Select(index => index * 10L + 9L), displayedCounters);
        await bridge.CloseAsync(SessionId, CancellationToken.None);
    }

    private static readonly Guid SessionId = Guid.Parse("4dba22cc-a6ed-4278-9548-b3591ee580e8");

    private static AcquisitionAnalysisBatch AnalysisBatch(long firstSampleCounter)
    {
        var stream = new AcquisitionStreamMetadata(
            "device",
            "device",
            1_000,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow);
        return new AcquisitionAnalysisBatch(
            SessionId,
            stream,
            new AcquisitionBatch(firstSampleCounter, 10, 2, Values(firstSampleCounter), DateTimeOffset.UtcNow),
            null,
            string.Empty);
    }

    private static double[] Values(long firstSampleCounter) => Enumerable.Range(0, 10)
        .SelectMany(offset => new[] { (firstSampleCounter + offset) * 1e-6, firstSampleCounter + offset })
        .ToArray();

    private sealed class ControllableLiveFilterHandler : HttpMessageHandler
    {
        public TaskCompletionSource WarmupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWarmup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/sessions", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new { warmup_sample_count = 0, warmup_values_v = Array.Empty<double>() }),
                };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/warmup/binary", StringComparison.Ordinal))
            {
                WarmupStarted.TrySetResult();
                await ReleaseWarmup.Task.WaitAsync(cancellationToken);
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.Add("X-Warmup-Sample-Count", "10");
                response.Headers.Add("X-Unit", "V");
                return response;
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/batches/binary", StringComparison.Ordinal))
            {
                var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                };
                response.Headers.Add("X-Sample-Count", "10");
                response.Headers.Add("X-Unit", "V");
                return response;
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

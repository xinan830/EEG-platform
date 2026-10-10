using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.UiTests;

// Test-only transport: identity, not a scientific filter implementation.
internal sealed class IdentityFilterHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/sessions", StringComparison.Ordinal))
            return new(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { warmup_sample_count = 0, warmup_values_v = Array.Empty<double>() }),
            };
        if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
        if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/batches/binary", StringComparison.Ordinal))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var count = int.Parse(query["sample_count"] ?? throw new InvalidDataException("Missing sample count."));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(await request.Content!.ReadAsByteArrayAsync(cancellationToken)),
            };
            response.Headers.Add("X-Sample-Count", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            response.Headers.Add("X-Unit", "V");
            return response;
        }
        throw new InvalidOperationException($"Unexpected test request: {request.Method} {uri.AbsolutePath}");
    }
}

internal sealed class MemorySettings(string root) : IAcquisitionSettingsStore
{
    public AcquisitionConnectionSettings Current { get; private set; } = AcquisitionConnectionSettings.CreateDefault() with
    {
        SdkLibraryPath = "", RecordingDirectory = root,
    };
    public AcquisitionConnectionSettings Load() => Current;
    public Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken)
    {
        Current = settings;
        return Task.CompletedTask;
    }
}

internal sealed class SyntheticDriver : IAcquisitionDeviceDriver
{
    public AcquisitionDriverDescriptor Descriptor { get; } = new(AntEegoAcquisitionDriver.DriverId,
        "Synthetic UI test", "No physical hardware or SDK");
    public AcquisitionDeviceDescriptor Device { get; } = new("ui-test:1", "Synthetic UI test", "synthetic", [1000],
        [0.15], [0.375], Enumerable.Range(0, 30).Select(index => new AcquisitionChannelCapability(index,
            index < 24 ? AcquisitionChannelKind.Reference : index < 28 ? AcquisitionChannelKind.Bipolar
                : index == 28 ? AcquisitionChannelKind.Trigger : AcquisitionChannelKind.SampleCounter,
            index < 28 ? "V" : index == 28 ? "code" : "count")).ToArray(),
        Model: "Synthetic ANT layout", DriverId: AntEegoAcquisitionDriver.DriverId);
    public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) => new Adapter(this, configuration);

    public static double Value(int native, long counter) => native == 29 ? counter : native == 28 ? 0
        : ((native switch { 2 => 20, 3 => 40, 5 => 60, _ => 10 })
           + 4 * Math.Sin(2 * Math.PI * (8 + native) * counter / 1000d)) * 1e-6;

    private sealed class Adapter(SyntheticDriver driver, AcquisitionDriverConfiguration configuration) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Synthetic only");
        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>([driver.Device]);
        public Task<IAcquisitionStream> OpenEegStreamAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IAcquisitionStream>(new Stream(driver.Device, configuration, request.SamplingRateHz));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Stream(AcquisitionDeviceDescriptor device, AcquisitionDriverConfiguration configuration, int rate) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new(device.DeviceId, device.DisplayName, rate,
            device.ChannelCapabilities!.Select((channel, index) => new AcquisitionChannel(index, channel.NativeChannelIndex,
                configuration.ChannelLabelsByNativeIndex?.GetValueOrDefault(channel.NativeChannelIndex), channel.Kind, channel.Unit)).ToArray(),
            29, DateTimeOffset.UtcNow) { HardwareConfiguration = configuration.DriverSettings };
        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            for (long first = 0; await timer.WaitForNextTickAsync(cancellationToken); first += 50)
            {
                var values = new double[50 * 30];
                for (var sample = 0; sample < 50; sample++)
                    for (var native = 0; native < 30; native++)
                        values[sample * 30 + native] = Value(native, first + sample);
                yield return new AcquisitionBatch(first, 50, 30, values, DateTimeOffset.UtcNow);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

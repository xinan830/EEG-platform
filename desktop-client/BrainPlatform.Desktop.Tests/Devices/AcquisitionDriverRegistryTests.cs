using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Drivers;
using BrainPlatform.Desktop.Acquisition.Runtime;
using BrainPlatform.Desktop.Domain;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionDriverRegistryTests
{
    [Fact]
    public async Task Runtime_ConfiguresAndDiscoversThroughANonAntDriver()
    {
        var driver = new TestDriver();
        var registry = new AcquisitionDriverRegistry([driver]);
        await using var runtime = new ConfiguredAcquisitionRuntime(driverRegistry: registry);

        await runtime.ConfigureAsync(
            new AcquisitionDriverConfiguration("test-amplifier", new Dictionary<string, string>
            {
                ["endpoint"] = "local-test",
            }),
            CancellationToken.None);
        var devices = await runtime.DiscoverAsync(CancellationToken.None);

        Assert.Equal("test-amplifier", driver.LastConfiguration?.DriverId);
        Assert.Equal("local-test", driver.LastConfiguration?.RequireSetting("endpoint"));
        Assert.Equal("Test amplifier", Assert.Single(devices).DisplayName);
        Assert.Equal("test-amplifier", Assert.Single(runtime.AvailableDrivers).DriverId);
    }

    [Fact]
    public void Registry_RejectsDuplicateDriverIds()
    {
        Assert.Throws<ArgumentException>(() => new AcquisitionDriverRegistry([
            new TestDriver(),
            new TestDriver(),
        ]));
    }

    [Fact]
    public void Registry_RejectsAnUninstalledDriverWithoutCreatingAnAdapter()
    {
        var registry = new AcquisitionDriverRegistry([new TestDriver()]);

        Assert.Throws<AcquisitionUnavailableException>(() => registry.CreateAdapter(
            new AcquisitionDriverConfiguration("not-installed", new Dictionary<string, string>())));
    }

    private sealed class TestDriver : IAcquisitionDeviceDriver
    {
        public AcquisitionDriverDescriptor Descriptor { get; } = new(
            "test-amplifier",
            "Test amplifier",
            "Test-only driver.");

        public AcquisitionDriverConfiguration? LastConfiguration { get; private set; }

        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration)
        {
            LastConfiguration = configuration;
            return new TestAdapter();
        }
    }

    private sealed class TestAdapter : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Test adapter is configured.");

        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>([
                new AcquisitionDeviceDescriptor("test:1", "Test amplifier", null, [500]),
            ]);

        public Task<IAcquisitionStream> OpenEegStreamAsync(
            AcquisitionStreamRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<IAcquisitionStream>(new NotSupportedException("Not needed by this test."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

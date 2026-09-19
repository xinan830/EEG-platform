using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Drivers;
using BrainPlatform.Desktop.Acquisition.Runtime;
using BrainPlatform.Desktop.Acquisition.Session;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Domain;
using BrainPlatform.Desktop.ViewModels;
using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class DeviceSessionManagerTests
{
    [Fact]
    public async Task Discovery_SelectsTheOnlyReturnedDeviceAndPublishesStableCapabilities()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);

        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);

        Assert.Equal(DeviceSessionState.Connected, session.Snapshot.State);
        Assert.Equal("test-device", session.Snapshot.DriverId);
        Assert.Equal("test:1", session.SelectedDevice?.DeviceId);
        Assert.Equal("test-device", session.SelectedDevice?.DriverId);
        Assert.Equal("test-model", session.SelectedDevice?.Model);
        Assert.NotNull(session.Snapshot.CapabilityFingerprint);
        Assert.True(session.IsReadyForSetup);
    }

    [Fact]
    public async Task DiscoveryWithNoDeviceClearsThePriorSelection()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);

        driver.Devices = [];
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);

        Assert.Empty(session.Devices);
        Assert.Null(session.SelectedDevice);
        Assert.Null(session.Snapshot.CapabilityFingerprint);
        Assert.Equal(DeviceSessionState.Disconnected, session.Snapshot.State);
        Assert.False(session.IsReadyForSetup);
    }

    [Fact]
    public async Task AvailabilityRefresh_AfterIdleDeviceRemoval_ClearsTheStaleConnectedDevice()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);

        driver.Devices = [];
        await session.RefreshAvailabilityAsync(CancellationToken.None);

        Assert.Empty(session.Devices);
        Assert.Null(session.SelectedDevice);
        Assert.Equal(DeviceSessionState.Disconnected, session.Snapshot.State);
    }

    [Fact]
    public async Task StartConfirmation_RechecksTheSelectedDeviceThroughTheSdk()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);

        driver.Devices = [];

        await Assert.ThrowsAsync<AcquisitionUnavailableException>(
            () => session.ConfirmSelectedDeviceAvailabilityAsync(CancellationToken.None));
        Assert.Null(session.SelectedDevice);
        Assert.Equal(DeviceSessionState.Disconnected, session.Snapshot.State);
    }

    [Fact]
    public async Task AvailabilityRefresh_WithEquivalentDiscovery_PreservesPublishedDeviceAndSnapshot()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);
        var selectedBeforeRefresh = Assert.IsType<AcquisitionDeviceDescriptor>(session.SelectedDevice);
        var snapshotBeforeRefresh = session.Snapshot;
        var snapshotChanges = 0;
        session.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(DeviceSessionManager.Snapshot))
            {
                snapshotChanges++;
            }
        };

        // Drivers commonly materialize fresh descriptor/list instances on every probe.
        driver.Devices = [CreateDevice()];
        await session.RefreshAvailabilityAsync(CancellationToken.None);

        Assert.Same(selectedBeforeRefresh, session.SelectedDevice);
        Assert.Same(snapshotBeforeRefresh, session.Snapshot);
        Assert.Same(selectedBeforeRefresh, Assert.Single(session.Devices));
        Assert.Equal(0, snapshotChanges);
    }

    [Fact]
    public async Task AvailabilityRefresh_WithChangedCapabilities_ReplacesThePublishedDevice()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);
        var selectedBeforeRefresh = Assert.IsType<AcquisitionDeviceDescriptor>(session.SelectedDevice);
        var fingerprintBeforeRefresh = session.Snapshot.CapabilityFingerprint;

        driver.Devices =
        [
            CreateDevice() with
            {
                ChannelCapabilities =
                [
                    new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
                    new AcquisitionChannelCapability(1, AcquisitionChannelKind.Bipolar, "V"),
                    new AcquisitionChannelCapability(2, AcquisitionChannelKind.SampleCounter, "count"),
                ],
            },
        ];
        await session.RefreshAvailabilityAsync(CancellationToken.None);

        Assert.NotSame(selectedBeforeRefresh, session.SelectedDevice);
        Assert.NotEqual(fingerprintBeforeRefresh, session.Snapshot.CapabilityFingerprint);
        Assert.Equal(DeviceSessionState.Connected, session.Snapshot.State);
    }

    [Fact]
    public void CapabilityFingerprint_IsIndependentOfReturnedChannelOrder()
    {
        var first = DeviceSessionManager.CreateCapabilityFingerprint(
            "test-device",
            [
                new AcquisitionChannelCapability(2, AcquisitionChannelKind.Bipolar, "V"),
                new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
            ]);
        var second = DeviceSessionManager.CreateCapabilityFingerprint(
            "test-device",
            [
                new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannelCapability(2, AcquisitionChannelKind.Bipolar, "V"),
            ]);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ChannelProfileEditor_ResolvesTheSharedSelectedDeviceWithoutNavigationInput()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);
        var mapping = new ChannelMappingViewModel(new ChannelLabelMappingStore(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            mapping,
            new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json")),
            session);
        workspace.Profiles.Add(new ChannelConfigurationProfile(
            "c2d1fba7e29f40a2bbcf4ca63d90e075",
            "测试配置",
            "",
            ChannelConfigurationSource.User,
            "0:Reference",
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        workspace.SelectedProfile = Assert.Single(workspace.Profiles);

        workspace.BeginEditSelected();

        Assert.Equal(session.SelectedDevice?.DeviceId, workspace.DraftDevice?.DeviceId);
        Assert.Equal("F3", Assert.Single(workspace.DraftRows).ElectrodeLabel);
    }

    [Fact]
    public async Task ReferencedChannelProfile_NewVersionReusesTheDiscoveredDeviceDescriptor()
    {
        var driver = new TestDriver([CreateDevice()]);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);
        var selectedDevice = Assert.IsType<AcquisitionDeviceDescriptor>(session.SelectedDevice);
        var mapping = new ChannelMappingViewModel(new ChannelLabelMappingStore(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));
        mapping.LoadDevice(selectedDevice);
        var workspace = new ChannelConfigurationWorkspaceViewModel(
            mapping,
            new ChannelConfigurationProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json")),
            session);
        workspace.Profiles.Add(new ChannelConfigurationProfile(
            Guid.NewGuid().ToString("N"),
            "被引用配置",
            "",
            ChannelConfigurationSource.User,
            mapping.GetDeviceSignature(),
            [new ChannelConfigurationEntry(0, AcquisitionChannelKind.Reference, "F3", true, 0)],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)
        {
            MontageReferenceCount = 1,
            IsCompatibleWithCurrentDevice = true,
        });
        workspace.SelectedProfile = Assert.Single(workspace.Profiles);

        workspace.BeginNewVersionSelected();

        Assert.Same(selectedDevice, workspace.DraftDevice);
        Assert.Equal(selectedDevice.DeviceId, workspace.DraftDevice?.DeviceId);
        Assert.Equal("F3", Assert.Single(workspace.DraftRows).ElectrodeLabel);
        Assert.True(workspace.CanEditSignalDraft);
    }

    [Fact]
    public async Task RuntimeFault_UpdatesTheSharedDeviceSession()
    {
        var driver = new TestDriver([CreateDevice()], faultWhenStreaming: true);
        await using var runtime = CreateRuntime(driver);
        using var session = new DeviceSessionManager(runtime);
        await session.DiscoverAsync(CreateConfiguration(), CancellationToken.None);
        await session.ConfigureForStreamAsync(CreateConfiguration(), CancellationToken.None);
        var faulted = new TaskCompletionSource<DeviceSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(DeviceSessionManager.Snapshot) &&
                session.Snapshot.State == DeviceSessionState.Faulted)
            {
                faulted.TrySetResult(session.Snapshot);
            }
        };
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await runtime.StartAsync(
                new AcquisitionStreamRequest(
                    session.SelectedDevice!.DeviceId,
                    500,
                    new AcquisitionProjectContext("project", "P001", "Test", directory, "{}")),
                CancellationToken.None);

            var snapshot = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(DeviceSessionState.Faulted, snapshot.State);
            Assert.Equal("test:1", snapshot.SelectedDevice?.DeviceId);
            Assert.Contains("device removed", snapshot.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ConfiguredAcquisitionRuntime CreateRuntime(TestDriver driver) => new(
        driverRegistry: new AcquisitionDriverRegistry([driver]));

    private static AcquisitionDriverConfiguration CreateConfiguration() => new(
        "test-device",
        new Dictionary<string, string> { ["endpoint"] = "test" });

    private static AcquisitionDeviceDescriptor CreateDevice() => new(
        "test:1",
        "Test device",
        "test-serial",
        [500, 1000],
        ChannelCapabilities:
        [
            new AcquisitionChannelCapability(0, AcquisitionChannelKind.Reference, "V"),
            new AcquisitionChannelCapability(1, AcquisitionChannelKind.SampleCounter, "count"),
        ],
        Model: "test-model");

    private sealed class TestDriver(
        IReadOnlyList<AcquisitionDeviceDescriptor> devices,
        bool faultWhenStreaming = false) : IAcquisitionDeviceDriver
    {
        public AcquisitionDriverDescriptor Descriptor { get; } = new(
            "test-device",
            "Test device",
            "Test-only device session driver.");

        public IReadOnlyList<AcquisitionDeviceDescriptor> Devices { get; set; } = devices;

        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) =>
            new TestAdapter(this, faultWhenStreaming);
    }

    private sealed class TestAdapter(TestDriver driver, bool faultWhenStreaming) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Test adapter is configured.");

        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(driver.Devices);

        public Task<IAcquisitionStream> OpenEegStreamAsync(
            AcquisitionStreamRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IAcquisitionStream>(new TestStream(faultWhenStreaming));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestStream(bool faultWhenStreaming) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new(
            "test:1",
            "Test device",
            500,
            [
                new AcquisitionChannel(0, 0, null, AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, null, AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow);

        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (faultWhenStreaming)
            {
                throw new IOException("device removed");
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

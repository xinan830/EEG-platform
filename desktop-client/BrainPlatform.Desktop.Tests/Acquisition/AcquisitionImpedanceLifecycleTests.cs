using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionImpedanceLifecycleTests
{
    [Fact]
    public async Task DetectionBlocksDiscoverySelectionReconfigurationAndEeg_AndClosesOnce()
    {
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        using var devices = new DeviceSessionManager(runtime);
        await devices.DiscoverAsync(Configuration(), default);
        var selected = devices.SelectedDevice;
        var received = new TaskCompletionSource<ImpedanceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = 0;
        runtime.ImpedanceUpdated += (_, value) => { if (Interlocked.Increment(ref updates) == 2) received.TrySetResult(value); };

        await runtime.StartImpedanceAsync(Request(), null, null, default);
        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, snapshot.UpdateSequence);
        Assert.Equal(2500, snapshot.Readings[0].ResistanceOhms);
        Assert.Equal(DeviceSessionState.Impedance, devices.Snapshot.State);
        Assert.Same(selected, devices.SelectedDevice);
        var probes = driver.Adapter.Probes;
        await devices.RefreshAvailabilityAsync(default);
        await Assert.ThrowsAsync<AcquisitionDeviceBusyException>(() => devices.DiscoverAsync(Configuration(), default));
        Assert.Throws<InvalidOperationException>(() => devices.SelectDevice(selected));
        await Assert.ThrowsAsync<AcquisitionDeviceBusyException>(() => runtime.ConfigureAsync(Configuration(), default));
        await Assert.ThrowsAsync<AcquisitionDeviceBusyException>(() => runtime.StartPreviewAsync(EegRequest(), default));
        Assert.Equal(probes, driver.Adapter.Probes);
        Assert.Empty(runtime.GetDisplaySnapshot());
        Assert.Null(runtime.StreamMetadata);
        Assert.Equal(0, driver.Adapter.EegOpens);

        await runtime.StopAsync(default);
        await runtime.StopImpedanceAsync();
        Assert.Equal(AcquisitionDeviceMode.Idle, runtime.DeviceMode.Mode);
        Assert.Equal(1, driver.Adapter.Impedance.CloseCount);
        Assert.Equal(DeviceSessionState.Connected, devices.Snapshot.State);
        await runtime.StartPreviewAsync(EegRequest(), default);
        Assert.Equal(AcquisitionState.Previewing, runtime.State.State);
        Assert.Null(runtime.RecordingSessionId);
        await runtime.StopAsync(default);
    }

    [Fact]
    public async Task StaleConfirmationCannotStopAnActivePreview()
    {
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        await runtime.ConfigureAsync(Configuration(), default);
        var id = await runtime.StartPreviewAsync(EegRequest(), default);
        await Assert.ThrowsAsync<AcquisitionUnavailableException>(() => runtime.StartImpedanceAsync(Request(), Guid.NewGuid(), null, default));
        Assert.Equal(id, runtime.State.SessionId);
        Assert.Equal(AcquisitionState.Previewing, runtime.State.State);
        Assert.Equal(0, driver.Adapter.ImpedanceOpens);
        await runtime.StopAsync(default);
    }

    [Fact]
    public async Task RecordingAndPausedRecordingRejectImpedance_WithoutChangingTheRecording()
    {
        var directory = Path.Combine(Path.GetTempPath(), "eeg-impedance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        await runtime.ConfigureAsync(Configuration(), default);
        var firstSession = await runtime.StartPreviewAsync(EegRequest(directory), default);
        await runtime.StartRecordingAsync(default);
        var recording = runtime.RecordingSessionId;
        var recordDirectory = runtime.RecordingDirectory;
        await Assert.ThrowsAsync<AcquisitionUnavailableException>(() => runtime.StartImpedanceAsync(Request(), firstSession, recording, default));
        Assert.Equal(AcquisitionState.Recording, runtime.State.State);
        Assert.Equal(recording, runtime.RecordingSessionId);
        Assert.Equal(recordDirectory, runtime.RecordingDirectory);
        Assert.Equal(0, driver.Adapter.ImpedanceOpens);

        await runtime.PauseAsync(default);
        await Assert.ThrowsAsync<AcquisitionUnavailableException>(() => runtime.StartImpedanceAsync(Request(), firstSession, recording, default));
        Assert.Equal(AcquisitionState.Paused, runtime.State.State);
        Assert.Equal(recording, runtime.RecordingSessionId);
        Assert.Equal(0, driver.Adapter.ImpedanceOpens);

        await runtime.ResumeAsync(default);
        await runtime.StopAsync(default);
        await runtime.StartImpedanceAsync(Request(), runtime.State.SessionId, runtime.RecordingSessionId, default);
        await runtime.StopImpedanceAsync();
        var secondSession = await runtime.StartPreviewAsync(EegRequest(directory), default);
        Assert.NotEqual(firstSession, secondSession);
        Assert.Null(runtime.RecordingSessionId);
        Assert.Empty(runtime.GetDisplaySnapshot());
        await runtime.StopAsync(default);
    }

    [Fact]
    public async Task FailedCloseKeepsModeOccupiedUntilSuccessfulRetry()
    {
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        await runtime.ConfigureAsync(Configuration(), default);
        await runtime.StartImpedanceAsync(Request(), null, null, default);
        driver.Adapter.Impedance.FailClose = true;
        await Assert.ThrowsAsync<IOException>(() => runtime.StopImpedanceAsync());
        Assert.Equal(AcquisitionDeviceMode.ImpedanceFaulted, runtime.DeviceMode.Mode);
        await Assert.ThrowsAsync<AcquisitionDeviceBusyException>(() => runtime.StartPreviewAsync(EegRequest(), default));
        driver.Adapter.Impedance.FailClose = false;
        await runtime.StopImpedanceAsync();
        Assert.Equal(AcquisitionDeviceMode.Idle, runtime.DeviceMode.Mode);
        Assert.Equal(1, driver.Adapter.Impedance.CloseCount);
    }

    [Fact]
    public async Task OpenCancellationDoesNotPublishDetection_AndReleasesThroughTheAdapter()
    {
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        await runtime.ConfigureAsync(Configuration(), default);
        driver.Adapter.FailOpen = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.StartImpedanceAsync(Request(), null, null, default));
        Assert.Equal(AcquisitionDeviceMode.ImpedanceFaulted, runtime.DeviceMode.Mode);
        await runtime.StopImpedanceAsync();
        Assert.True(driver.Adapter.Disposed);
        Assert.Equal(AcquisitionDeviceMode.Idle, runtime.DeviceMode.Mode);
    }

    [Fact]
    public async Task QueuedAvailabilityRefreshCannotEnumerateAfterImpedanceWinsTheTransition()
    {
        var driver = new Driver();
        await using var runtime = Runtime(driver);
        using var devices = new DeviceSessionManager(runtime);
        await devices.DiscoverAsync(Configuration(), default);
        var original = devices.SelectedDevice;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        driver.Adapter.BeforeImpedanceOpen = () =>
        {
            entered.SetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        };
        var opening = runtime.StartImpedanceAsync(Request(), null, null, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var probes = driver.Adapter.Probes;
        var discovery = runtime.DiscoverAsync(default);
        await devices.RefreshAvailabilityAsync(default);
        release.SetResult();
        await opening;
        await Assert.ThrowsAsync<AcquisitionDeviceBusyException>(() => discovery);
        Assert.Equal(probes, driver.Adapter.Probes);
        Assert.Same(original, devices.SelectedDevice);
        Assert.Equal(DeviceSessionState.Impedance, devices.Snapshot.State);
        await runtime.StopImpedanceAsync();
    }

    [Fact]
    public async Task ReaderIgnoresWrongSessionAndJoinsBeforeReleasingTheStream()
    {
        var id = Guid.NewGuid();
        var stream = new ControlledStream(id);
        var session = new ImpedanceReadingSession(stream, id);
        var accepted = new List<Guid>();
        session.Updated += (_, snapshot) => accepted.Add(snapshot.DetectionSessionId);
        session.Start();
        await stream.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stopping = session.StopAsync();
        Assert.False(stopping.IsCompleted);
        Assert.False(stream.Disposed);
        stream.Release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(accepted);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ImpedanceDoesNotContactBackendOrCreateEegSessions()
    {
        var driver = new Driver();
        var handler = new RejectRequests();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var runtime = new ConfiguredAcquisitionRuntime(client, new([driver]));
        await runtime.ConfigureAsync(Configuration(), default);
        await runtime.StartImpedanceAsync(Request(), null, null, default);
        await runtime.StopImpedanceAsync();
        Assert.Equal(0, handler.Count);
        Assert.Null(runtime.RecordingDirectory);
        Assert.Null(runtime.State.SessionId);
        Assert.Empty(runtime.GetDisplaySnapshot());
    }

    private static ConfiguredAcquisitionRuntime Runtime(Driver driver) => new(driverRegistry: new([driver]));
    private static AcquisitionDriverConfiguration Configuration() => new("impedance-test", new Dictionary<string, string>());
    private static ImpedanceStreamRequest Request() => new(Guid.NewGuid(), "test:1", [new(0, AcquisitionChannelKind.Reference)]);
    private static AcquisitionStreamRequest EegRequest(string? directory = null) => new("test:1", 500,
        new AcquisitionProjectContext("test-project", "P001", "Fake project", directory ?? Path.GetTempPath(), "{}"));

    private sealed class Driver : IAcquisitionDeviceDriver
    {
        public Adapter Adapter { get; } = new();
        public AcquisitionDriverDescriptor Descriptor { get; } = new("impedance-test", "Impedance test", "Fake");
        public IAcquisitionDeviceAdapter CreateAdapter(AcquisitionDriverConfiguration configuration) => Adapter;
    }
    private sealed class Adapter : IAcquisitionDeviceAdapter, IAcquisitionImpedanceDeviceAdapter
    {
        public ImpedanceStream Impedance { get; } = new();
        public int Probes { get; private set; }
        public int EegOpens { get; private set; }
        public int ImpedanceOpens { get; private set; }
        public bool EegClosed { get; set; }
        public bool Disposed { get; private set; }
        public bool FailOpen { get; set; }
        public Action? BeforeImpedanceOpen { get; set; }
        public DeviceReadiness GetReadiness() => new(true, "Ready", "Fake");
        public ImpedanceCapabilityStatus ImpedanceCapability => ImpedanceCapabilityStatus.Unknown;
        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken)
        {
            Probes++;
            return Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>([new("test:1", "Fake", null, [500],
                ChannelCapabilities: [new(0, AcquisitionChannelKind.Reference, "V"), new(1, AcquisitionChannelKind.SampleCounter, "count")])]);
        }
        public Task<IAcquisitionStream> OpenEegStreamAsync(AcquisitionStreamRequest request, CancellationToken cancellationToken)
        {
            EegOpens++;
            EegClosed = false;
            return Task.FromResult<IAcquisitionStream>(new EegStream(() => EegClosed = true));
        }
        public Task<IAcquisitionImpedanceStream> OpenImpedanceStreamAsync(ImpedanceStreamRequest request, CancellationToken cancellationToken)
        {
            BeforeImpedanceOpen?.Invoke();
            ImpedanceOpens++;
            if (FailOpen) throw new OperationCanceledException();
            Impedance.SessionId = request.DetectionSessionId;
            return Task.FromResult<IAcquisitionImpedanceStream>(Impedance);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class ImpedanceStream : IAcquisitionImpedanceStream
    {
        public Guid SessionId { get; set; }
        public int CloseCount { get; private set; }
        public bool FailClose { get; set; }
        public ImpedanceStreamMetadata Metadata { get; } = new("test:1", "Fake", [new(0, 0, AcquisitionChannelKind.Reference, "Fz")]);
        public async IAsyncEnumerable<ImpedanceSnapshot> ReadSnapshotsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long sequence = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return ImpedanceSnapshot.Create(SessionId, ++sequence, DateTimeOffset.UtcNow, Metadata, [2500d]);
                await Task.Delay(10, cancellationToken);
            }
        }
        public ValueTask DisposeAsync()
        {
            if (FailClose) throw new IOException("Native close failed");
            CloseCount++;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class EegStream(Action closed) : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new("test:1", "Fake", 500,
            [new(0, 0, "Fz", AcquisitionChannelKind.Reference, "V"),
             new(1, 1, null, AcquisitionChannelKind.SampleCounter, "count")], 1, DateTimeOffset.UtcNow);
        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
        public ValueTask DisposeAsync() { closed(); return ValueTask.CompletedTask; }
    }
    private sealed class RejectRequests : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            throw new InvalidOperationException("Impedance must not use the backend");
        }
    }
    private sealed class ControlledStream(Guid id) : IAcquisitionImpedanceStream
    {
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public ImpedanceStreamMetadata Metadata { get; } = new("test:1", "Fake", [new(0, 0, AcquisitionChannelKind.Reference, "Fz")]);
        public async IAsyncEnumerable<ImpedanceSnapshot> ReadSnapshotsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return ImpedanceSnapshot.Create(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, Metadata, [1000d]);
            ReadEntered.SetResult();
            await Release.Task;
            yield return ImpedanceSnapshot.Create(id, 2, DateTimeOffset.UtcNow, Metadata, [1000d]);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}

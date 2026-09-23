using BrainPlatform.Desktop.Acquisition.Analysis;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Runtime;
using BrainPlatform.Desktop.Acquisition.Storage;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class AcquisitionCoordinatorTests
{
    [Fact]
    public async Task Coordinator_PersistsRawDataBeforeCompletingAndAuditsCounterGap()
    {
        var directory = CreateTempDirectory();
        await using var stream = new TestStream();
        await using var coordinator = new AcquisitionCoordinator(
            new TestAdapter(stream),
            new LocalAcquisitionRawWriterFactory(),
            new NullAcquisitionAnalysisBridge(),
            ringBufferCapacitySamples: 8);

        try
        {
            var devices = await coordinator.DiscoverAsync(CancellationToken.None);
            var sessionId = await coordinator.StartAsync(
                Request(devices[0].DeviceId, directory),
                CancellationToken.None);

            await WaitForStateAsync(coordinator, AcquisitionState.Stopped);

            Assert.NotEqual(Guid.Empty, sessionId);
            Assert.True(stream.WasDisposed);
            var sessionDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(directory, "recordings")));
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "samples-000001.bin")));
            using (var manifest = System.Text.Json.JsonDocument.Parse(
                       await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "manifest.json"))))
            {
                var project = manifest.RootElement.GetProperty("Project");
                Assert.Equal("project", project.GetProperty("Id").GetString());
                Assert.Equal("P001", project.GetProperty("Number").GetString());
                Assert.Equal("Test", project.GetProperty("Name").GetString());
            }
            var audit = await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "audit.jsonl"));
            Assert.Contains("sample_gap", audit);
            Assert.Contains("completed", audit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_DoesNotPretendAnUnavailableAdapterCanStart()
    {
        await using var coordinator = new AcquisitionCoordinator(
            new UnavailableAcquisitionDeviceAdapter(),
            new LocalAcquisitionRawWriterFactory(),
            new NullAcquisitionAnalysisBridge());

        var directory = CreateTempDirectory();
        try
        {
            await Assert.ThrowsAsync<AcquisitionUnavailableException>(() => coordinator.StartAsync(
                Request("missing", directory),
                CancellationToken.None));
            Assert.Equal(AcquisitionState.Faulted, coordinator.State.State);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_PauseSkipsRawDataButKeepsTheLiveDisplayAndAuditsThePausedCounterRange()
    {
        var directory = CreateTempDirectory();
        await using var stream = new ControlledTestStream();
        await using var coordinator = new AcquisitionCoordinator(
            new TestAdapter(stream),
            new LocalAcquisitionRawWriterFactory(),
            new NullAcquisitionAnalysisBridge(),
            ringBufferCapacitySamples: 16);

        try
        {
            var device = Assert.Single(await coordinator.DiscoverAsync(CancellationToken.None));
            await coordinator.StartAsync(Request(device.DeviceId, directory), CancellationToken.None);

            await stream.WriteAsync(Batch(0, 2));
            await stream.WaitForYieldCountAsync(1);
            await WaitForDisplayLastSampleCounterAsync(coordinator, 1);

            await coordinator.PauseAsync(CancellationToken.None);
            Assert.Equal(AcquisitionState.Paused, coordinator.State.State);

            await stream.WriteAsync(Batch(2, 2));
            await stream.WriteAsync(Batch(4, 2));
            await stream.WaitForYieldCountAsync(3);
            await WaitForDisplayLastSampleCounterAsync(coordinator, 5);

            await coordinator.ResumeAsync(CancellationToken.None);
            Assert.Equal(AcquisitionState.Recording, coordinator.State.State);
            await stream.WriteAsync(Batch(6, 2));
            await stream.WaitForYieldCountAsync(4);
            await WaitForDisplayLastSampleCounterAsync(coordinator, 7);
            var displayed = coordinator.GetDisplaySnapshot();
            Assert.Equal(8, displayed.Sum(batch => batch.SampleCount));
            Assert.Equal(Enumerable.Range(0, 8).Select(value => (long)value), displayed
                .SelectMany(batch => Enumerable.Range(0, batch.SampleCount)
                    .Select(offset => batch.FirstSampleCounter + offset)));
            stream.Complete();
            await WaitForStateAsync(coordinator, AcquisitionState.Stopped);

            var sessionDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(directory, "recordings")));
            var rawFile = new FileInfo(Path.Combine(sessionDirectory, "samples-000001.bin"));
            Assert.Equal(112, rawFile.Length); // Two persisted 2-sample, 2-channel batches.
            var audit = await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "audit.jsonl"));
            Assert.Contains("\"FirstMissingSampleCounter\":2", audit);
            Assert.Contains("\"LastMissingSampleCounter\":5", audit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_PreviewDoesNotCreateARecordingUntilRecordingIsExplicitlyStarted()
    {
        var directory = CreateTempDirectory();
        await using var stream = new ControlledTestStream();
        await using var coordinator = new AcquisitionCoordinator(
            new TestAdapter(stream),
            new LocalAcquisitionRawWriterFactory(),
            new NullAcquisitionAnalysisBridge(),
            ringBufferCapacitySamples: 16);

        try
        {
            var device = Assert.Single(await coordinator.DiscoverAsync(CancellationToken.None));
            var previewSessionId = await coordinator.StartPreviewAsync(
                Request(device.DeviceId, directory),
                CancellationToken.None);

            Assert.Equal(AcquisitionState.Previewing, coordinator.State.State);
            Assert.False(Directory.Exists(Path.Combine(directory, "recordings")));
            await stream.WriteAsync(Batch(0, 2));
            await stream.WaitForYieldCountAsync(1);
            await WaitForDisplayLastSampleCounterAsync(coordinator, 1);

            var recordingSessionId = await coordinator.StartRecordingAsync(CancellationToken.None);
            Assert.NotEqual(previewSessionId, recordingSessionId);
            Assert.Equal(AcquisitionState.Recording, coordinator.State.State);
            Assert.Single(Directory.GetDirectories(Path.Combine(directory, "recordings")));
            Assert.Single(coordinator.GetDisplaySnapshot());

            await stream.WriteAsync(Batch(2, 2));
            await stream.WaitForYieldCountAsync(2);
            await WaitForDisplayLastSampleCounterAsync(coordinator, 3);
            Assert.Equal(2, coordinator.RecordingFirstSampleCounter);
            await coordinator.StopAsync(CancellationToken.None);

            var sessionDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(directory, "recordings")));
            using var raw = new BinaryReader(File.OpenRead(Path.Combine(sessionDirectory, "samples-000001.bin")));
            Assert.Equal(2, raw.ReadInt64());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_StoppingPreviewDoesNotCreateAnEmptyRecording()
    {
        var directory = CreateTempDirectory();
        await using var stream = new ControlledTestStream();
        await using var coordinator = new AcquisitionCoordinator(
            new TestAdapter(stream),
            new LocalAcquisitionRawWriterFactory(),
            new NullAcquisitionAnalysisBridge());

        try
        {
            var device = Assert.Single(await coordinator.DiscoverAsync(CancellationToken.None));
            await coordinator.StartPreviewAsync(Request(device.DeviceId, directory), CancellationToken.None);
            await coordinator.StopAsync(CancellationToken.None);

            Assert.Equal(AcquisitionState.Stopped, coordinator.State.State);
            Assert.False(Directory.Exists(Path.Combine(directory, "recordings")));
            Assert.True(stream.WasDisposed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitForStateAsync(AcquisitionCoordinator coordinator, AcquisitionState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (coordinator.State.State != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForDisplayLastSampleCounterAsync(
        AcquisitionCoordinator coordinator,
        long expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (coordinator.LatestDisplaySampleCounter != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static AcquisitionBatch Batch(long firstCounter, int sampleCount) =>
        new(firstCounter, sampleCount, 2, Enumerable.Range(0, sampleCount)
            .SelectMany(offset => new[] { (firstCounter + offset + 1) * 1e-6, (double)(firstCounter + offset) })
            .ToArray(), DateTimeOffset.UtcNow);

    private static AcquisitionStreamRequest Request(string deviceId, string directory) =>
        new(deviceId, 500, new AcquisitionProjectContext("project", "P001", "Test", directory, "{}"));

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestAdapter(IAcquisitionStream stream) : IAcquisitionDeviceAdapter
    {
        public DeviceReadiness GetReadiness() => new(true, "ready", "test adapter");

        public Task<IReadOnlyList<AcquisitionDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcquisitionDeviceDescriptor>>(
            [new AcquisitionDeviceDescriptor("test-device", "test device", null, [500])]);

        public Task<IAcquisitionStream> OpenEegStreamAsync(
            AcquisitionStreamRequest request,
            CancellationToken cancellationToken) => Task.FromResult<IAcquisitionStream>(stream);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestStream : IAcquisitionStream
    {
        public AcquisitionStreamMetadata Metadata { get; } = new(
            "test-device",
            "test device",
            500,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 31, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow);

        public bool WasDisposed { get; private set; }

        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new AcquisitionBatch(0, 2, 2, [1e-6, 0, 2e-6, 1], DateTimeOffset.UtcNow);
            await Task.Yield();
            yield return new AcquisitionBatch(4, 2, 2, [3e-6, 4, 4e-6, 5], DateTimeOffset.UtcNow);
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledTestStream : IAcquisitionStream
    {
        private readonly Channel<AcquisitionBatch> batches = Channel.CreateUnbounded<AcquisitionBatch>();
        private int yieldedCount;

        public bool WasDisposed { get; private set; }

        public AcquisitionStreamMetadata Metadata { get; } = new(
            "test-device",
            "test device",
            500,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 31, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow);

        public Task WriteAsync(AcquisitionBatch batch) => batches.Writer.WriteAsync(batch).AsTask();

        public void Complete() => batches.Writer.TryComplete();

        public async Task WaitForYieldCountAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Volatile.Read(ref yieldedCount) < expected)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public async IAsyncEnumerable<AcquisitionBatch> ReadBatchesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var batch in batches.Reader.ReadAllAsync(cancellationToken))
            {
                Interlocked.Increment(ref yieldedCount);
                yield return batch;
            }
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            Complete();
            return ValueTask.CompletedTask;
        }
    }
}

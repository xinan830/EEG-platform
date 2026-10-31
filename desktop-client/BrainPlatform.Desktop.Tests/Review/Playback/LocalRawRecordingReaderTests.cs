using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class LocalRawRecordingReaderTests
{
    [Fact]
    public async Task OpenAsyncIndexesHeadersWithoutReadingPayloadAndReadsOnlyRequestedWindow()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [
                MakeBatch(100, 4, [1, 10, 100, 0, 2, 20, 101, 0, 3, 30, 102, 0, 4, 40, 103, 0]),
                MakeBatch(104, 4, [5, 50, 104, 0, 6, 60, 105, 0, 7, 70, 106, 0, 8, 80, 107, 0])]);

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, sessionId, CancellationToken.None);

            Assert.Equal(10, recording.Manifest.SamplingRateHz);
            Assert.NotEqual(Guid.Empty, recording.Manifest.SessionId);
            Assert.Equal(["F3", "F4", "Trigger", "Counter"], recording.Manifest.Channels.Select(channel => channel.Label));
            Assert.Equal(["V", "V", "code", "count"], recording.Manifest.Channels.Select(channel => channel.Unit));
            Assert.Equal(2, recording.Index.Batches.Count);
            Assert.Equal(0, recording.Index.PayloadBytesReadDuringOpen);

            var window = (await recording.ReadSamplesAsync(
                new RecordingSampleRange(102, 105), null, CancellationToken.None)).Window;

            var segment = Assert.Single(window.Segments);
            Assert.Equal(0.2, window.ActualStartSeconds);
            Assert.Equal(0.5, window.ActualEndSeconds);
            Assert.Equal(102, segment.FirstSampleCounter);
            Assert.Equal(3, segment.SampleCount);
            Assert.Equal([3d, 30d, 102d, 0d, 4d, 40d, 103d, 0d, 5d, 50d, 104d, 0d], segment.SampleMajorValues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadWindowKeepsCounterGapAsSeparateSegments()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [MakeBatch(100, 2, [double.NaN, 0, 110, 0, 2, 0, 111, 0]), MakeBatch(104, 2, [3, 0, 114, 0, 4, 0, 115, 0])]);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "audit.jsonl"),
                "{\"type\":\"sample_gap\",\"FirstMissingSampleCounter\":102,\"LastMissingSampleCounter\":103,\"MissingSampleCount\":2}\n");

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, sessionId, CancellationToken.None);
            var window = (await recording.ReadSamplesAsync(
                new RecordingSampleRange(100, 106), null, CancellationToken.None)).Window;

            Assert.Equal(2, window.Segments.Count);
            Assert.Equal(0, window.ActualStartSeconds);
            Assert.Equal(0.6, window.ActualEndSeconds);
            Assert.Equal(100, window.Segments[0].FirstSampleCounter);
            Assert.Equal(104, window.Segments[1].FirstSampleCounter);
            Assert.True(double.IsNaN(window.Segments[0].SampleMajorValues[0]));
            Assert.Equal(2, Assert.Single(recording.Gaps).MissingSampleCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsyncRejectsTruncatedPayloadWithStructuredCode()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 0, samplingRateHz: 10);
            using (var stream = File.Create(Path.Combine(directory, "samples-000001.bin")))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0L);
                writer.Write(4);
                writer.Write(4);
                writer.Write(DateTimeOffset.UtcNow.UtcTicks);
                writer.Write(1d);
            }

            var exception = await Assert.ThrowsAsync<RecordingReadException>(
                () => LocalRawRecordingReader.OpenAsync(directory, sessionId, CancellationToken.None));

            Assert.Equal("RECORDING_CHUNK_TRUNCATED", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsyncRejectsManifestForDifferentSelectedRecording()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifestSessionId = WriteManifest(directory, firstSampleCounter: 0, samplingRateHz: 10);
            await File.WriteAllBytesAsync(Path.Combine(directory, "samples-000001.bin"), [0]);
            var selectedSessionId = Guid.NewGuid();

            var exception = await Assert.ThrowsAsync<RecordingReadException>(
                () => LocalRawRecordingReader.OpenAsync(directory, selectedSessionId, CancellationToken.None));

            Assert.Equal("RECORDING_IDENTITY_MISMATCH", exception.Code);
            Assert.NotEqual(manifestSessionId, selectedSessionId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadSamplesUsesAbsoluteCountersAndExplicitChannelSelection()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [MakeBatch(100, 4,
                [1, 10, 100, 0, 2, 20, 101, 0, 3, 30, 102, 0, 4, 40, 103, 0])]);

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, sessionId, CancellationToken.None);
            var result = await recording.ReadSamplesAsync(
                new RecordingSampleRange(101, 104), [1, 3], CancellationToken.None);

            Assert.Equal(new RecordingSampleRange(101, 104), result.ActualRange);
            Assert.Equal(["F4", "Counter"], result.Channels.Select(channel => channel.Label));
            var segment = Assert.Single(result.Window.Segments);
            Assert.Equal([20d, 0d, 30d, 0d, 40d, 0d], segment.SampleMajorValues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadSamplesRejectsRangeWithNoAvailableData()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [MakeBatch(100, 2, [1, 0, 0, 0, 2, 0, 0, 0])]);
            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, sessionId, CancellationToken.None);

            var exception = await Assert.ThrowsAsync<RecordingAccessException>(() =>
                recording.ReadSamplesAsync(new RecordingSampleRange(200, 201), null, CancellationToken.None));

            Assert.Equal(RecordingAccessErrorCodes.NoData, exception.Code);
            Assert.Equal(sessionId, exception.RecordingId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SessionManagerSharesSourceUntilAllLeasesClose()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sessionId = WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [MakeBatch(100, 2, [1, 0, 0, 0, 2, 0, 0, 0])]);
            await using var manager = new RecordingSessionManager();
            await using var first = await manager.OpenLocalAsync(directory, sessionId, CancellationToken.None);
            await using var second = await manager.OpenLocalAsync(directory, sessionId, CancellationToken.None);

            Assert.Equal(first.Metadata.Revision, second.Metadata.Revision);
            await first.ReadSamplesAsync(new RecordingSampleRange(100, 102), null, CancellationToken.None);
            await first.DisposeAsync();
            await second.ReadSamplesAsync(new RecordingSampleRange(100, 102), null, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Guid WriteManifest(string directory, long firstSampleCounter, int samplingRateHz)
    {
        var sessionId = Guid.NewGuid();
        var manifest = new
        {
            SessionId = sessionId,
            PayloadFormat = "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
            EegSignalUnit = "V",
            DeviceId = "test-device",
            DeviceName = "Test amplifier",
            SamplingRateHz = samplingRateHz,
            SampleCounterChannelIndex = 3,
            RecordingStartUtc = DateTimeOffset.UtcNow,
            Channels = new[]
            {
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 1, "F4", AcquisitionChannelKind.Bipolar, "V"),
                new AcquisitionChannel(2, 2, "Trigger", AcquisitionChannelKind.Trigger, "code"),
                new AcquisitionChannel(3, 3, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            },
            Project = new AcquisitionProjectContext("project", "P001", "Test project", directory, "{}"),
            HardwareConfiguration = new Dictionary<string, string>
            {
                ["montage_configuration_id"] = "montage-a",
                ["montage_configuration_name"] = "导联 A",
                ["montage_configuration_snapshot_json"] = "{}",
            },
            FirstSampleCounter = firstSampleCounter,
        };
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest));
        return sessionId;
    }

    private static void WriteChunk(string directory, int index, params Batch[] batches)
    {
        using var stream = File.Create(Path.Combine(directory, $"samples-{index:D6}.bin"));
        using var writer = new BinaryWriter(stream);
        foreach (var batch in batches)
        {
            writer.Write(batch.FirstSampleCounter);
            writer.Write(batch.SampleCount);
            writer.Write(batch.Values.Length / batch.SampleCount);
            writer.Write(DateTimeOffset.UtcNow.UtcTicks);
            foreach (var value in batch.Values)
            {
                writer.Write(value);
            }
        }
    }

    private static Batch MakeBatch(long firstSampleCounter, int sampleCount, double[] values) =>
        new(firstSampleCounter, sampleCount, values);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-review-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record Batch(long FirstSampleCounter, int SampleCount, double[] Values);
}

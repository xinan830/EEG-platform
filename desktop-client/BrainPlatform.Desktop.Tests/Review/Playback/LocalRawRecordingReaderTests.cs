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
            WriteManifest(directory, firstSampleCounter: 100, samplingRateHz: 10);
            WriteChunk(directory, 1, [
                MakeBatch(100, 4, [1, 10, 100, 0, 2, 20, 101, 0, 3, 30, 102, 0, 4, 40, 103, 0]),
                MakeBatch(104, 4, [5, 50, 104, 0, 6, 60, 105, 0, 7, 70, 106, 0, 8, 80, 107, 0])]);

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, CancellationToken.None);

            Assert.Equal(10, recording.Manifest.SamplingRateHz);
            Assert.Equal(2, recording.Index.Batches.Count);
            Assert.Equal(0, recording.Index.PayloadBytesReadDuringOpen);

            var window = await recording.Reader.ReadWindowAsync(0.2, 0.3, CancellationToken.None);

            var segment = Assert.Single(window.Segments);
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
            WriteManifest(directory, firstSampleCounter: 0, samplingRateHz: 10);
            WriteChunk(directory, 1, [MakeBatch(0, 2, [1, 0, 10, 0, 2, 0, 11, 0]), MakeBatch(4, 2, [3, 0, 14, 0, 4, 0, 15, 0])]);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "audit.jsonl"),
                "{\"type\":\"sample_gap\",\"FirstMissingSampleCounter\":2,\"LastMissingSampleCounter\":3,\"MissingSampleCount\":2}\n");

            await using var recording = await LocalRawRecordingReader.OpenAsync(directory, CancellationToken.None);
            var window = await recording.Reader.ReadWindowAsync(0, 0.6, CancellationToken.None);

            Assert.Equal(2, window.Segments.Count);
            Assert.Equal(0, window.Segments[0].FirstSampleCounter);
            Assert.Equal(4, window.Segments[1].FirstSampleCounter);
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
            WriteManifest(directory, firstSampleCounter: 0, samplingRateHz: 10);
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
                () => LocalRawRecordingReader.OpenAsync(directory, CancellationToken.None));

            Assert.Equal("RECORDING_CHUNK_TRUNCATED", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteManifest(string directory, long firstSampleCounter, int samplingRateHz)
    {
        var manifest = new
        {
            SessionId = Guid.NewGuid(),
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

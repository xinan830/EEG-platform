using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Storage;
using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LocalAcquisitionRawWriterTests
{
    [Fact]
    public async Task Writer_PersistsManifestChunksAndExplicitGapAudit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var metadata = Metadata();
            var factory = new LocalAcquisitionRawWriterFactory(maxChunkBytes: 56);
            await using var writer = await factory.CreateAsync(
                Guid.NewGuid(),
                metadata,
                new AcquisitionProjectContext("project", "P001", "Test", directory, "{}"),
                directory,
                CancellationToken.None);
            var sessionDirectory = writer.RecordingDirectory;

            await writer.AppendBatchAsync(Batch(0), CancellationToken.None);
            await writer.AppendGapAsync(new AcquisitionGap(2, 3, 2, DateTimeOffset.UtcNow), CancellationToken.None);
            await writer.AppendBatchAsync(Batch(4), CancellationToken.None);
            await writer.CompleteAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.NotEqual(directory, sessionDirectory);
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "samples-000001.bin")));
            Assert.True(File.Exists(Path.Combine(sessionDirectory, "samples-000002.bin")));
            using var firstChunk = new BinaryReader(File.OpenRead(Path.Combine(sessionDirectory, "samples-000001.bin")));
            Assert.Equal(0, firstChunk.ReadInt64());
            Assert.Equal(2, firstChunk.ReadInt32());
            Assert.Equal(2, firstChunk.ReadInt32());
            Assert.NotEqual(0, firstChunk.ReadInt64());
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "manifest.json")));
            Assert.Equal("test", manifest.RootElement.GetProperty("HardwareConfiguration").GetProperty("source").GetString());
            var audit = await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "audit.jsonl"));
            Assert.Contains("sample_gap", audit);
            Assert.Contains("completed", audit);

            var records = new LocalRecordingCatalog().Read(directory);
            Assert.Single(records);
            Assert.Equal("已完成", records[0].Status);
            Assert.Equal(sessionDirectory, records[0].RecordingDirectory);
            Assert.Equal(1, records[0].SignalChannelCount);
            Assert.Equal(1, records[0].AuxiliaryChannelCount);
            Assert.Equal(2, records[0].StreamColumnCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AcquisitionStreamMetadata Metadata() =>
        new(
            "test-device",
            "test device",
            500,
            [
                new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
                new AcquisitionChannel(1, 31, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
            ],
            1,
            DateTimeOffset.UtcNow)
        {
            HardwareConfiguration = new Dictionary<string, string> { ["source"] = "test" },
        };

    private static AcquisitionBatch Batch(long firstSampleCounter) =>
        new(firstSampleCounter, 2, 2, [1e-6, 0, 2e-6, 1], DateTimeOffset.UtcNow);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

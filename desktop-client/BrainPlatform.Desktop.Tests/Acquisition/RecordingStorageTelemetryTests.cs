using System.IO;
using BrainPlatform.Desktop.Modules.Acquisition.Storage;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class RecordingStorageTelemetryTests
{
    [Fact]
    public void Sample_MissingDirectory_ReportsUnavailableBytesWithoutZeroFabrication()
    {
        var snapshot = RecordingStorageTelemetry.Sample(null);

        Assert.Null(snapshot.RecordedBytes);
        Assert.Null(snapshot.AvailableFreeBytes);
        Assert.False(snapshot.IsLowDiskSpace);
        Assert.Equal("不可用", RecordingStorageTelemetry.FormatBytes(snapshot.RecordedBytes));
    }

    [Fact]
    public void Sample_ExistingRecordingDirectory_SumsFileSizesAndReportsFreeSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-platform-storage-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "manifest.json"), new byte[256]);
            File.WriteAllBytes(Path.Combine(root, "chunk-0001.bin"), new byte[2048]);

            var snapshot = RecordingStorageTelemetry.Sample(root);

            Assert.Equal(2304, snapshot.RecordedBytes);
            Assert.NotNull(snapshot.AvailableFreeBytes);
            Assert.True(snapshot.AvailableFreeBytes > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "不可用")]
    [InlineData(-1L, "不可用")]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(5L * 1024L * 1024L, "5.0 MB")]
    [InlineData(3L * 1024L * 1024L * 1024L, "3.0 GB")]
    public void FormatBytes_FormatsExpectedUnits(long? bytes, string expected)
    {
        Assert.Equal(expected, RecordingStorageTelemetry.FormatBytes(bytes));
    }
}

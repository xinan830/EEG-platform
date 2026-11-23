using System.Globalization;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Acquisition.Storage;

public sealed record RecordingStorageSnapshot(
    long? RecordedBytes,
    long? AvailableFreeBytes,
    bool IsLowDiskSpace);

/// <summary>
/// Samples on-disk size of an active raw recording directory and remaining
/// free space on its target volume. Missing or unreadable paths report null
/// instead of zero so the UI never fabricates healthy storage figures.
/// </summary>
public static class RecordingStorageTelemetry
{
    public const long LowDiskSpaceThresholdBytes = 2L * 1024L * 1024L * 1024L;

    public static RecordingStorageSnapshot Sample(string? recordingDirectory)
    {
        if (string.IsNullOrWhiteSpace(recordingDirectory))
        {
            return new RecordingStorageSnapshot(null, null, false);
        }

        long? recordedBytes = null;
        long? availableFreeBytes = null;

        try
        {
            var fullPath = Path.GetFullPath(recordingDirectory);
            if (Directory.Exists(fullPath))
            {
                var total = 0L;
                foreach (var file in new DirectoryInfo(fullPath).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total = checked(total + file.Length);
                    }
                    catch (IOException)
                    {
                        // Concurrent chunk rotation or transient file lock; skip only that entry.
                    }
                }

                recordedBytes = total;
            }

            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    availableFreeBytes = drive.AvailableFreeSpace;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Explicit unavailable state is returned via null fields.
        }

        var isLowDisk = availableFreeBytes is { } free && free < LowDiskSpaceThresholdBytes;
        return new RecordingStorageSnapshot(recordedBytes, availableFreeBytes, isLowDisk);
    }

    public static string FormatBytes(long? bytes)
    {
        if (bytes is not { } value || value < 0)
        {
            return "不可用";
        }

        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        const double gigabyte = megabyte * 1024d;

        if (value >= gigabyte)
        {
            return $"{(value / gigabyte).ToString("0.0", CultureInfo.InvariantCulture)} GB";
        }

        if (value >= megabyte)
        {
            return $"{(value / megabyte).ToString("0.0", CultureInfo.InvariantCulture)} MB";
        }

        if (value >= kilobyte)
        {
            return $"{(value / kilobyte).ToString("0.0", CultureInfo.InvariantCulture)} KB";
        }

        return $"{value.ToString(CultureInfo.InvariantCulture)} B";
    }
}

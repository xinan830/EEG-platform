using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Export;

/// <summary>Stages backend artifacts and restores existing files if publication fails.</summary>
public static class RecordingReviewSignalExporter
{
    public static async Task ExportAsync(string targetPath, string format,
        Func<Stream, CancellationToken, Task> download, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (format is not ("edf" or "bdf")) throw new ArgumentOutOfRangeException(nameof(format));
        targetPath = Path.GetFullPath(targetPath);
        var staging = Path.Combine(Path.GetDirectoryName(targetPath)!, $".signal-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var preserveRecovery = false;
        try
        {
            var zipPath = Path.Combine(staging, "export.zip");
            await using (var stream = File.Create(zipPath))
                await download(stream, cancellationToken).ConfigureAwait(false);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var expected = new[] { $"recording.{format}", $"recording.{format}.labels.json" };
                if (archive.Entries.Count != 2 || !archive.Entries.Select(item => item.FullName).Order()
                        .SequenceEqual(expected.Order()))
                    throw new InvalidDataException("导出数据包成员不完整或包含未知文件。");
                foreach (var name in expected)
                {
                    await using var source = archive.GetEntry(name)!.Open();
                    await using var destination = File.Create(Path.Combine(staging, name));
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
                using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(staging, expected[1]), cancellationToken).ConfigureAwait(false));
                if (metadata.RootElement.GetProperty("schema_version").GetString() != "recording-signal-export-v2")
                    throw new InvalidDataException("不支持的导出元数据版本。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { Publish(staging, targetPath, format); }
            catch (AggregateException)
            {
                preserveRecovery = true;
                throw;
            }
        }
        finally
        {
            if (!preserveRecovery) Directory.Delete(staging, recursive: true);
        }
    }

    private static void Publish(string staging, string targetPath, string format)
    {
        var destinations = new[] { targetPath, targetPath + ".labels.json" };
        var moved = new bool[2];
        var backedUp = new bool[2];
        try
        {
            for (var index = 0; index < destinations.Length; index++)
            {
                if (File.Exists(destinations[index]))
                {
                    File.Move(destinations[index], Path.Combine(staging, $"backup-{index}"));
                    backedUp[index] = true;
                }
                File.Move(Path.Combine(staging, $"recording.{format}" + (index == 1 ? ".labels.json" : "")), destinations[index]);
                moved[index] = true;
            }
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            for (var index = destinations.Length - 1; index >= 0; index--)
            {
                try
                {
                    if (moved[index]) File.Delete(destinations[index]);
                    if (backedUp[index]) File.Move(Path.Combine(staging, $"backup-{index}"), destinations[index]);
                }
                catch (Exception recoveryError) { errors.Add(recoveryError); }
            }
            if (errors.Count > 1)
                throw new AggregateException($"导出失败且回滚未完成，原文件备份保存在 {staging}", errors);
            throw;
        }
    }
}

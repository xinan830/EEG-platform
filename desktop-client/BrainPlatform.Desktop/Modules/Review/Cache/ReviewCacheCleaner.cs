using System.IO;

namespace BrainPlatform.Desktop.Modules.Review.Cache;

public sealed record LocalCacheClearResult(int DeletedFiles, long FreedBytes);

/// <summary>Deletes only this user's rebuildable review files, with no open review owners.</summary>
public sealed class ReviewCacheCleaner(string? applicationDataRoot = null)
{
    private readonly string root = applicationDataRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrainPlatform");

    public Task<LocalCacheClearResult> ClearAsync() => Task.Run(() =>
    {
        var roots = new[] { Path.Combine(root, "review-cache"), Path.Combine(root, "review-display-cache") };
        var files = new List<FileInfo>();
        // Validate both trees before deleting either; never follow a junction into a recording.
        foreach (var directory in roots)
        {
            for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
                RejectLink(parent);
            if (Directory.Exists(directory)) CollectFiles(new DirectoryInfo(directory), files);
        }
        var count = 0;
        long bytes = 0;
        foreach (var file in files)
        {
            var size = file.Length;
            file.Delete();
            count++;
            bytes += size;
        }
        foreach (var directory in roots)
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return new LocalCacheClearResult(count, bytes);
    });

    private static void CollectFiles(DirectoryInfo directory, List<FileInfo> files)
    {
        RejectLink(directory);
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            RejectLink(entry);
            if (entry is DirectoryInfo child) CollectFiles(child, files);
            else if (entry is FileInfo file) files.Add(file);
        }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null || (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new IOException("回放缓存目录包含链接，未执行本地清理。");
    }
}

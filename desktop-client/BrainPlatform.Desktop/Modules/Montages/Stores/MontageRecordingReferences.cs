using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Montages.Stores;

/// <summary>Verifies existing references without modifying historical recording files.</summary>
internal sealed class MontageRecordingReferences(string path, ResearchProjectStore projects)
{
    public async Task<MontageReferenceIndex> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MontageReferenceIndex index;
        try { index = await MontageReferenceIndex.ReadAsync(path, cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw VerificationError(path, exception);
        }
        var original = JsonSerializer.Serialize(index);
        foreach (var project in await projects.LoadAsync(cancellationToken))
        {
            index.Roots.Add(MontageReferenceIndex.NormalizePath(project.RecordingsDirectory));
            index.Roots.Add(MontageReferenceIndex.NormalizePath(Path.Combine(project.DirectoryPath, ".trash", "recordings")));
        }
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in index.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!DirectoryPresent(root)) continue;
                var directories = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(file =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return IsRecordingFile(file);
                    })
                    .Select(file => MontageReferenceIndex.NormalizePath(Path.GetDirectoryName(file)!))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var directory in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ReadRecordingAsync(index, directory, cancellationToken);
                    discovered.Add(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw VerificationError(root, exception);
            }
        }
        foreach (var directory in index.Recordings.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!discovered.Contains(directory)) await ReadRecordingAsync(index, directory, cancellationToken);
        }
        index.HistoricalIds.UnionWith(index.ExistingIds);
        index.HistoricalIds.UnionWith(index.UnlocatedIds);
        index.ExistingIds.UnionWith(index.UnlocatedIds);
        if (index.NeedsMigration || original != JsonSerializer.Serialize(index)) await SaveAsync(index, cancellationToken);
        return index;
    }

    private static async Task ReadRecordingAsync(MontageReferenceIndex index, string directory, CancellationToken token)
    {
        try
        {
            if (!DirectoryPresent(directory)) return;
            var manifest = Path.Combine(directory, "manifest.json");
            string json;
            try { json = await File.ReadAllTextAsync(manifest, token); }
            catch (FileNotFoundException) when (!Directory.EnumerateFileSystemEntries(directory).Any()) { return; }
            using var document = JsonDocument.Parse(json);
            var metadata = document.RootElement.TryGetProperty("HardwareConfiguration", out var hardware)
                ? hardware.Deserialize<Dictionary<string, string>>() ?? throw new JsonException("HardwareConfiguration is null.")
                : new Dictionary<string, string>();
            var ids = MontageReferenceMetadata.From(metadata).ReadIds();
            if (index.Recordings.TryGetValue(directory, out var expected) && !expected.SetEquals(ids))
                throw new InvalidOperationException("记录导联引用与已登记信息不一致。");
            index.Register(ids, directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw VerificationError(directory, exception);
        }
    }

    private static bool DirectoryPresent(string directory)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory))!;
        // A disconnected volume is not proof that its recordings were deleted.
        if (!Directory.Exists(root)) throw new IOException($"记录所在磁盘不可访问：{root}");
        try { return File.GetAttributes(directory).HasFlag(FileAttributes.Directory); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsRecordingFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("audit.jsonl", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("recording-summary.json", StringComparison.OrdinalIgnoreCase) ||
            (name.StartsWith("samples-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
    }

    private static InvalidOperationException VerificationError(string location, Exception cause) =>
        new($"无法验证历史导联引用：{location}。请检查记录、回收目录或磁盘；暂不允许修改或删除。", cause);

    public Task SaveAsync(MontageReferenceIndex index, CancellationToken token) => index.SaveAsync(path, token);
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Montages.Stores;

/// <summary>Historical edit locks and discoverable recording locations have different lifetimes.</summary>
internal sealed class MontageReferenceIndex
{
    [JsonRequired] public int Version { get; init; } = 2;
    [JsonRequired] public HashSet<string> HistoricalIds { get; set; } = new(StringComparer.Ordinal);
    [JsonRequired] public HashSet<string> UnlocatedIds { get; set; } = new(StringComparer.Ordinal);
    [JsonRequired] public HashSet<string> Roots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonRequired] public Dictionary<string, HashSet<string>> Recordings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public HashSet<string> ExistingIds { get; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public bool NeedsMigration { get; init; }

    public void Register(IReadOnlyList<string> ids, string? recordingDirectory,
        IEnumerable<string>? roots = null)
    {
        var directory = recordingDirectory is null ? null : NormalizePath(recordingDirectory);
        if (directory is not null && Recordings.TryGetValue(directory, out var expected) && !expected.SetEquals(ids))
            throw new InvalidOperationException("记录导联引用与已登记信息不一致。");
        HistoricalIds.UnionWith(ids);
        ExistingIds.UnionWith(ids);
        if (directory is null) UnlocatedIds.UnionWith(ids);
        else Recordings[directory] = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var root in roots ?? []) Roots.Add(NormalizePath(root));
    }

    public static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static async Task<MontageReferenceIndex> ReadAsync(string path, CancellationToken token)
    {
        string json;
        try { json = await File.ReadAllTextAsync(path, token); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var ids = document.RootElement.Deserialize<HashSet<string>>()
                ?? throw new InvalidOperationException("导联历史引用索引为空。");
            if (ids.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("导联引用 ID 无效。");
            return new() { HistoricalIds = ids, NeedsMigration = true };
        }
        var index = document.RootElement.Deserialize<MontageReferenceIndex>()
            ?? throw new InvalidOperationException("导联历史引用索引为空。");
        if (index.Version != 2 || index.HistoricalIds is null || index.UnlocatedIds is null ||
            index.Roots is null || index.Recordings is null ||
            index.HistoricalIds.Concat(index.UnlocatedIds).Concat(index.Roots).Any(string.IsNullOrWhiteSpace) ||
            index.Recordings.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null ||
                pair.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("导联引用索引格式无效，无法验证删除权限。");
        if (!index.HistoricalIds.IsSupersetOf(index.UnlocatedIds.Concat(index.Recordings.Values.SelectMany(ids => ids))))
            throw new InvalidOperationException("导联引用索引缺少历史身份，无法验证删除权限。");
        index.Roots = new(index.Roots.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (directory, ids) in index.Recordings)
        {
            var key = NormalizePath(directory);
            if (normalized.TryGetValue(key, out var expected) && !expected.SetEquals(ids))
                throw new InvalidOperationException("导联引用索引存在冲突记录路径。");
            normalized[key] = ids;
        }
        index.Recordings = normalized;
        return index;
    }

    public async Task SaveAsync(string path, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(this), token);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

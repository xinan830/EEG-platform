using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Montages.Stores;

/// <summary>
/// Typed view of the montage fields carried in the recording hardware metadata.
/// The manifest remains dictionary-shaped for backward compatibility; parsing and
/// validation live here so stores and scanners do not duplicate string keys.
/// </summary>
internal sealed record MontageReferenceMetadata(string? MontageId, string? SnapshotJson, bool HasSnapshot)
{
    private const string MontageIdKey = "montage_configuration_id";
    private const string SnapshotKey = "montage_configuration_snapshot_json";

    public static MontageReferenceMetadata From(IReadOnlyDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var hasSnapshot = metadata.TryGetValue(SnapshotKey, out var snapshotJson);
        return new(
            GetValue(metadata, MontageIdKey),
            snapshotJson,
            hasSnapshot);
    }

    public IReadOnlyList<string> ReadIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(MontageId)) ids.Add(MontageId);
        if (!HasSnapshot || string.IsNullOrWhiteSpace(SnapshotJson)) return ids.ToArray();

        using var snapshot = JsonDocument.Parse(SnapshotJson);
        if (!snapshot.RootElement.TryGetProperty("Id", out var identity) ||
            string.IsNullOrWhiteSpace(identity.GetString()))
            throw new InvalidOperationException("历史导联快照缺少配置 ID，无法验证引用关系。");

        var snapshotId = identity.GetString()!;
        if (ids.Count > 0 && !ids.Contains(snapshotId))
            throw new InvalidOperationException("导联配置 ID 与历史快照 ID 不一致，无法验证引用关系。");
        ids.Add(snapshotId);
        return ids.ToArray();
    }

    public MontageProfile ReadSnapshot() =>
        JsonSerializer.Deserialize<MontageProfile>(SnapshotJson ?? string.Empty)
        ?? throw new InvalidOperationException("采集导联快照为空。");

    private static string? GetValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Montages.Stores;

/// <summary>Persists user-created montage profiles; recordings own their later snapshots.</summary>
public sealed class MontageProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;
    private readonly MontageRecordingReferences references;
    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    public MontageProfileStore(string? path = null, ResearchProjectStore? projectStore = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "montage-profiles.json");
        references = new MontageRecordingReferences($"{this.path}.references.json", projectStore ??
            (path is null ? new ResearchProjectStore() : new ResearchProjectStore($"{this.path}.projects.json")));
    }

    public async Task<IReadOnlyList<MontageProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var index = await references.LoadAsync(cancellationToken);
            return (await ReadProfilesAsync(cancellationToken))
                .Select(profile => profile with
                {
                    IsRecordingReferenced = index.HistoricalIds.Contains(profile.Id),
                    HasRecordingReferences = index.ExistingIds.Contains(profile.Id),
                }).ToArray();
        }
        finally { MutationGate.Release(); }
    }

    private async Task<IReadOnlyList<MontageProfile>> ReadProfilesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<MontageProfile>>(
                   stream, SerializerOptions, cancellationToken) ?? [];
    }

    public async Task<IReadOnlySet<string>> GetReferencedIdsAsync(CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try { return (await references.LoadAsync(cancellationToken)).HistoricalIds; }
        finally { MutationGate.Release(); }
    }

    public async Task SaveAsync(MontageProfile profile, CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var ids = (await references.LoadAsync(cancellationToken)).HistoricalIds;
            var profiles = (await ReadProfilesAsync(cancellationToken)).ToList();
            if (ids.Contains(profile.Id)) throw new InvalidOperationException("该导联配置已被历史记录引用，不能修改；请复制为新配置。");
            if (profile.Source != MontageProfileSource.User || profiles.Any(item => item.Id == profile.Id && !item.CanEdit))
                throw new InvalidOperationException("该导联配置只读，不能修改；请复制为新配置。");
            profiles.RemoveAll(existing => existing.Id == profile.Id);
            profiles.Add(profile);
            await WriteAsync(profiles, cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            if ((await references.LoadAsync(cancellationToken)).ExistingIds.Contains(id))
                throw new InvalidOperationException("该导联配置仍被现存记录或回收目录中的记录引用，不能删除；可以归档。");
            var profiles = await ReadProfilesAsync(cancellationToken);
            if (profiles.Any(profile => profile.Id == id && !profile.CanDelete))
                throw new InvalidOperationException("系统预设导联配置不能删除。");
            await WriteAsync(profiles.Where(profile => profile.Id != id).ToArray(), cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    public async Task ArchiveAsync(string id, CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadProfilesAsync(cancellationToken);
            if (!profiles.Any(profile => profile.Id == id && profile.CanArchive))
                throw new InvalidOperationException("此导联配置不能归档。");
            await WriteAsync(profiles.Select(profile => profile.Id == id ? profile with { IsArchived = true } : profile).ToArray(), cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    public async Task RegisterRecordingReferenceAsync(IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken, bool validateCurrentPreset = false, string? recordingDirectory = null)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            await RegisterReferenceCoreAsync(metadata, recordingDirectory, null, validateCurrentPreset, cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    /// <summary>Prevents deletion from observing the gap before the recording manifest is published.</summary>
    public async Task<T> PublishRecordingAsync<T>(IReadOnlyDictionary<string, string> metadata,
        string recordingDirectory, IReadOnlyList<string> referenceRoots, Func<Task<T>> publish,
        CancellationToken cancellationToken)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            await RegisterReferenceCoreAsync(metadata, recordingDirectory, referenceRoots, true, cancellationToken);
            return await publish();
        }
        finally { MutationGate.Release(); }
    }

    private async Task RegisterReferenceCoreAsync(IReadOnlyDictionary<string, string> metadata,
        string? recordingDirectory, IReadOnlyList<string>? referenceRoots,
        bool validateCurrentPreset, CancellationToken cancellationToken)
    {
        var recordingIds = MontageRecordingReferences.ReadIds(metadata);
        if (recordingIds.Count == 0) return;
        var index = await references.LoadAsync(cancellationToken);
        if (validateCurrentPreset && metadata.TryGetValue("montage_configuration_snapshot_json", out var json))
        {
            var snapshot = JsonSerializer.Deserialize<MontageProfile>(json)
                ?? throw new InvalidOperationException("采集导联快照为空。");
            var current = (await ReadProfilesAsync(cancellationToken)).FirstOrDefault(profile => profile.Id == snapshot.Id);
            if (snapshot.Source == MontageProfileSource.User && current is null)
                throw new InvalidOperationException("采集准备时的导联已删除，请重新选择导联并开始预览。");
            if (current is not null && (current.IsArchived || JsonSerializer.Serialize(current) != JsonSerializer.Serialize(snapshot)))
                throw new InvalidOperationException("采集准备时的导联已修改或归档，请重新选择导联并开始预览。");
        }
        index.Register(recordingIds, recordingDirectory, referenceRoots);
        await references.SaveAsync(index, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<MontageProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Montage profile path has no directory."));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, SerializerOptions, cancellationToken);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

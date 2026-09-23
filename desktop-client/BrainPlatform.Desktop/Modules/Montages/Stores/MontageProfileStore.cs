using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Configuration;

/// <summary>Persists user-created montage profiles; recordings own their later snapshots.</summary>
public sealed class MontageProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;

    public MontageProfileStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "montage-profiles.json");
    }

    public async Task<IReadOnlyList<MontageProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<MontageProfile>>(
                   stream, SerializerOptions, cancellationToken) ?? [];
    }

    public async Task SaveAsync(MontageProfile profile, CancellationToken cancellationToken)
    {
        var profiles = (await LoadAsync(cancellationToken)).ToList();
        profiles.RemoveAll(existing => existing.Id == profile.Id);
        profiles.Add(profile);
        await WriteAsync(profiles, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var profiles = (await LoadAsync(cancellationToken))
            .Where(profile => profile.Id != id)
            .ToArray();
        await WriteAsync(profiles, cancellationToken);
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

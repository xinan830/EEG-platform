using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Channels.Stores;

/// <summary>Persists user-created workstation presets, never recordings.</summary>
public sealed class ChannelConfigurationProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;

    public ChannelConfigurationProfileStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "channel-configuration-profiles.json");
    }

    public async Task<IReadOnlyList<ChannelConfigurationProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<ChannelConfigurationProfile> profiles;
        await using (var stream = File.OpenRead(path))
        {
            profiles = await JsonSerializer.DeserializeAsync<List<ChannelConfigurationProfile>>(
                           stream,
                           SerializerOptions,
                           cancellationToken)
                       ?? [];
        }

        // Older builds could save a user-edited system preset with the preset's
        // semantic ID. User profile IDs are UUIDs, so migrate that bad state
        // without touching the user's labels or display selection.
        var migrated = profiles
            .Select(profile => profile.Source == ChannelConfigurationSource.User &&
                               !Guid.TryParse(profile.Id, out _)
                ? profile with { Id = Guid.NewGuid().ToString("N") }
                : profile)
            .ToList();
        if (!profiles.SequenceEqual(migrated))
        {
            await WriteAsync(migrated, cancellationToken);
        }

        return migrated;
    }

    public async Task SaveAsync(ChannelConfigurationProfile profile, CancellationToken cancellationToken)
    {
        var profiles = (await LoadAsync(cancellationToken)).ToList();
        profiles.RemoveAll(existing => existing.Id == profile.Id);
        profiles.Add(profile);

        await WriteAsync(profiles, cancellationToken);
    }

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken)
    {
        var profiles = (await LoadAsync(cancellationToken))
            .Where(profile => profile.Id != profileId)
            .ToArray();
        await WriteAsync(profiles, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<ChannelConfigurationProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Profile path has no directory."));
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

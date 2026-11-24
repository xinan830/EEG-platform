using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Channels.Stores;

/// <summary>
/// Stores the exact profile snapshot that is currently applied to a compatible
/// device. It is separate from the editable profile catalogue so recordings
/// remain attributable even if the catalogue entry is later changed or deleted.
/// </summary>
public sealed class ActiveChannelConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;

    public ActiveChannelConfigurationStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "active-channel-configurations.json");
    }

    public bool TryLoad(string deviceSignature, out ActiveChannelConfiguration active)
    {
        active = default!;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, ActiveChannelConfiguration>>(File.ReadAllText(path)) ?? [];
            if (all.TryGetValue(deviceSignature, out var saved))
            {
                active = saved;
                return true;
            }

            return false;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local active channel configurations are not valid JSON.", exception);
        }
    }

    public async Task SaveAsync(ChannelConfigurationProfile profile, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Active configuration path has no directory.");
        Directory.CreateDirectory(directory);
        var all = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, ActiveChannelConfiguration>>(
                await File.ReadAllTextAsync(path, cancellationToken)) ?? []
            : [];
        all[profile.DeviceSignature] = new ActiveChannelConfiguration(profile, DateTimeOffset.UtcNow);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(all, SerializerOptions), cancellationToken);
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

    public async Task ClearAsync(string deviceSignature, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var all = JsonSerializer.Deserialize<Dictionary<string, ActiveChannelConfiguration>>(
            await File.ReadAllTextAsync(path, cancellationToken)) ?? [];
        if (!all.Remove(deviceSignature))
        {
            return;
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(all, SerializerOptions), cancellationToken);
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

public sealed record ActiveChannelConfiguration(
    ChannelConfigurationProfile Profile,
    DateTimeOffset AppliedAtUtc);

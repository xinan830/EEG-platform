using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Channels.Stores;

/// <summary>
/// Stores only user-confirmed physical channel to electrode-label mappings.
/// It never infers labels from device order or saves EEG samples.
/// </summary>
public sealed class ChannelLabelMappingStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string path;

    public ChannelLabelMappingStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "channel-label-mappings.json");
    }

    public IReadOnlyDictionary<int, string> Load(string deviceKey)
    {
        return TryLoad(deviceKey, out var mapping)
            ? mapping
            : new Dictionary<int, string>();
    }

    public bool TryLoad(string deviceKey, out IReadOnlyDictionary<int, string> mapping)
    {
        if (!File.Exists(path))
        {
            mapping = new Dictionary<int, string>();
            return false;
        }

        try
        {
            var allMappings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, string>>>(File.ReadAllText(path))
                ?? [];
            if (allMappings.TryGetValue(deviceKey, out var saved))
            {
                mapping = saved;
                return true;
            }

            mapping = new Dictionary<int, string>();
            return false;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local channel-label mappings are not valid JSON.", exception);
        }
    }

    public async Task SaveAsync(
        string deviceKey,
        IReadOnlyDictionary<int, string> mapping,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Mapping path has no directory.");
        Directory.CreateDirectory(directory);
        var allMappings = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, string>>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? []
            : [];
        allMappings[deviceKey] = mapping.ToDictionary(pair => pair.Key, pair => pair.Value);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(allMappings, SerializerOptions), cancellationToken);
    }
}

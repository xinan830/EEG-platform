using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Channels.Stores;

/// <summary>
/// Stores only a workstation's waveform-display choice. It does not alter
/// hardware acquisition, raw recordings, or electrode-label mappings.
/// </summary>
public sealed class ChannelDisplaySelectionStore
{
    private readonly string path;

    public ChannelDisplaySelectionStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "channel-display-selections.json");
    }

    public bool TryLoad(string deviceKey, out IReadOnlySet<int> selectedNativeChannelIndexes)
    {
        if (!File.Exists(path))
        {
            selectedNativeChannelIndexes = new HashSet<int>();
            return false;
        }

        try
        {
            var allSelections = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(path)) ?? [];
            if (allSelections.TryGetValue(deviceKey, out var saved))
            {
                selectedNativeChannelIndexes = saved.ToHashSet();
                return true;
            }

            selectedNativeChannelIndexes = new HashSet<int>();
            return false;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local channel display selections are not valid JSON.", exception);
        }
    }

    public async Task SaveAsync(
        string deviceKey,
        IReadOnlyCollection<int> selectedNativeChannelIndexes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Display selection path has no directory.");
        Directory.CreateDirectory(directory);
        var allSelections = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, int[]>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? []
            : [];
        allSelections[deviceKey] = selectedNativeChannelIndexes.Order().ToArray();
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(allSelections, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }
}

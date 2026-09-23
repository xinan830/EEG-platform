using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Events.Stores;

public sealed class EventDefinitionStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public EventDefinitionStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform", "event-definitions.json");
    }

    public async Task<IReadOnlyList<EventDefinition>> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(EventDefinition definition, CancellationToken cancellationToken)
    {
        EventValidation.ValidateDefinition(definition);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var definitions = (await ReadAsync(cancellationToken)).ToList();
            definitions.RemoveAll(item => item.Id == definition.Id);
            definitions.Add(definition);
            await WriteAsync(definitions, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var definitions = (await ReadAsync(cancellationToken)).Where(item => item.Id != id).ToArray();
            await WriteAsync(definitions, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<EventDefinition>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var envelope = await JsonSerializer.DeserializeAsync<EventStoreEnvelope<EventDefinition>>(stream, JsonOptions, cancellationToken);
        return envelope?.Items ?? [];
    }

    private async Task WriteAsync(IReadOnlyList<EventDefinition> definitions, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("事件定义路径无效。"));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, new EventStoreEnvelope<EventDefinition>(CurrentSchemaVersion, definitions), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

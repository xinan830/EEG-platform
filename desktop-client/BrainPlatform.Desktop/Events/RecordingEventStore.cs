using System.IO;
using System.Text.Json;

namespace BrainPlatform.Desktop.Events;

public sealed class RecordingEventStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public RecordingEventStore(string recordingDirectory)
    {
        if (string.IsNullOrWhiteSpace(recordingDirectory)) throw new ArgumentException("Recording directory is required.", nameof(recordingDirectory));
        path = Path.Combine(recordingDirectory, "events.json");
    }

    public async Task<IReadOnlyList<RecordingEvent>> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task UpsertAsync(RecordingEvent item, CancellationToken cancellationToken)
    {
        EventValidation.ValidateRecordingEvent(item);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadAsync(cancellationToken)).ToList();
            items.RemoveAll(existing => existing.Id == item.Id);
            items.Add(item);
            await WriteAsync(items, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadAsync(cancellationToken)).Where(item => item.Id != id).ToArray();
            await WriteAsync(items, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<RecordingEvent>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var envelope = await JsonSerializer.DeserializeAsync<EventStoreEnvelope<RecordingEvent>>(stream, JsonOptions, cancellationToken);
        return envelope?.Items ?? [];
    }

    private async Task WriteAsync(IReadOnlyList<RecordingEvent> items, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("事件记录路径无效。"));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, new EventStoreEnvelope<RecordingEvent>(CurrentSchemaVersion, items), JsonOptions, cancellationToken);
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

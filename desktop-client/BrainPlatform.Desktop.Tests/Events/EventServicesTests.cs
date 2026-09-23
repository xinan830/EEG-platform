using BrainPlatform.Desktop.Events;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class EventServicesTests
{
    [Fact]
    public async Task DefinitionStore_round_trips_and_rejects_duplicate_code()
    {
        using var temp = new TemporaryDirectory();
        var store = new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json"));
        var registry = new ShortcutRegistry();
        var service = new EventDefinitionService(store, registry);
        var first = Definition("EO", "睁眼", "#2563EB", "F1");
        await service.SaveAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<EventValidationException>(() => service.SaveAsync(Definition("EO", "重复", "#22C55E", "F2"), CancellationToken.None));
        var loaded = await service.ListAsync(CancellationToken.None);
        Assert.Equal(first, Assert.Single(loaded));
    }

    [Fact]
    public async Task Recording_event_keeps_definition_snapshot_when_definition_changes()
    {
        using var temp = new TemporaryDirectory();
        var definitions = new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json"));
        var definitionService = new EventDefinitionService(definitions);
        var definition = Definition("EO", "睁眼", "#2563EB", null);
        await definitionService.SaveAsync(definition, CancellationToken.None);
        var events = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), definitionService);

        var item = await events.CreateAsync(definition.Id, 100, 0, EventSource.ManualButton, "button", null, null, CancellationToken.None);
        await definitionService.SaveAsync(definition with { Name = "睁眼阶段", Color = "#22C55E", Version = 2, UpdatedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);

        var loaded = Assert.Single(await events.QueryAsync(new RecordingEventQuery(RecordingId: "recording-1"), CancellationToken.None));
        Assert.Equal(item.DefinitionSnapshot, loaded.DefinitionSnapshot);
        Assert.Equal("睁眼", loaded.DefinitionSnapshot.Name);
        Assert.Equal("#2563EB", loaded.DefinitionSnapshot.Color);
    }

    [Fact]
    public async Task Query_supports_combined_filters_and_disabled_definition_does_not_hide_history()
    {
        using var temp = new TemporaryDirectory();
        var definitionStore = new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json"));
        var definitionService = new EventDefinitionService(definitionStore);
        var definition = Definition("EMG", "肌电伪迹", "#EF4444", null);
        await definitionService.SaveAsync(definition, CancellationToken.None);
        var service = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), definitionService);
        await service.CreateAsync(definition.Id, 100, 50, EventSource.ManualButton, null, null, null, CancellationToken.None);
        await definitionService.DisableAsync(definition.Id, CancellationToken.None);

        var result = await service.QueryAsync(new RecordingEventQuery(
            RecordingId: "recording-1", StartSample: 120, EndSampleExclusive: 130,
            DefinitionCode: "EMG", Source: EventSource.ManualButton), CancellationToken.None);
        Assert.Single(result);
    }

    [Fact]
    public void Shortcut_registry_reports_conflict_and_releases_registration()
    {
        var registry = new ShortcutRegistry();
        Assert.True(registry.TryRegister("ctrl+1", ShortcutScope.Acquisition, "playback").Succeeded);
        var conflict = registry.TryRegister("CTRL + 1", ShortcutScope.Acquisition, "event-definition:eo");
        Assert.False(conflict.Succeeded);
        Assert.Equal("playback", conflict.Conflict!.ExistingCommand);
        registry.Unregister("Ctrl+1", ShortcutScope.Acquisition, "playback");
        Assert.True(registry.TryRegister("Ctrl+1", ShortcutScope.Acquisition, "event-definition:eo").Succeeded);
    }

    [Fact]
    public async Task Disabled_definition_releases_shortcut_and_external_event_is_read_only()
    {
        using var temp = new TemporaryDirectory();
        var definitionStore = new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json"));
        var registry = new ShortcutRegistry();
        var definitionService = new EventDefinitionService(definitionStore, registry);
        var definition = Definition("EO", "睁眼", "#2563EB", "F1");
        await definitionService.SaveAsync(definition, CancellationToken.None);
        await definitionService.DisableAsync(definition.Id, CancellationToken.None);
        Assert.False(registry.IsRegistered("F1", ShortcutScope.Acquisition));

        var service = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), definitionService);
        var imported = new RecordingEvent(
            "event-1", "recording-1", definition.Id,
            new EventDefinitionSnapshot("TRIGGER", "设备触发", "#EF4444", 1),
            EventSource.DeviceTrigger, "device", "11", 10, 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await new RecordingEventStore(temp.Path).UpsertAsync(imported, CancellationToken.None);
        await Assert.ThrowsAsync<EventValidationException>(() => service.DeleteAsync(imported.Id, CancellationToken.None));
    }

    private static EventDefinition Definition(string code, string name, string color, string? shortcut) => new(
        Guid.NewGuid().ToString("N"), code, name, string.Empty, color, true, shortcut,
        ShortcutScope.Acquisition, EventDefinitionSource.User, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brain-platform-events-" + Guid.NewGuid().ToString("N"));
        public string Path { get; } = string.Empty;
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}

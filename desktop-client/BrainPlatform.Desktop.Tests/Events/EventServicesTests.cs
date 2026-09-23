using BrainPlatform.Desktop.Events;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Storage;

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

    [Fact]
    public async Task External_source_adapters_preserve_codes_and_create_read_only_events()
    {
        using var temp = new TemporaryDirectory();
        var definition = Definition("STIM_A", "刺激 A", "#7C3AED", null);
        var definitionService = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        await definitionService.SaveAsync(definition, CancellationToken.None);
        var events = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), definitionService);

        var trigger = await new DeviceTriggerEventAdapter(events).RecordAsync(
            new DeviceTriggerEventInput(definition.Id, "TTL_11", 125, SourceDetail: "trigger-channel-28"),
            CancellationToken.None);
        var annotation = await new ImportedAnnotationEventAdapter(events).RecordAsync(
            new ImportedAnnotationEventInput(definition.Id, "EDF:eyes-open", 500, 250, Note: "imported from EDF+"),
            CancellationToken.None);

        Assert.Equal(EventSource.DeviceTrigger, trigger.Source);
        Assert.Equal("TTL_11", trigger.ExternalCode);
        Assert.Equal(EventSource.ImportedAnnotation, annotation.Source);
        Assert.True(annotation.IsInterval);
        await Assert.ThrowsAsync<EventValidationException>(() => events.DeleteAsync(annotation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Recording_events_round_trip_after_restart_and_manual_events_can_be_edited()
    {
        using var temp = new TemporaryDirectory();
        var definitionsPath = Path.Combine(temp.Path, "definitions.json");
        var definitionService = new EventDefinitionService(new EventDefinitionStore(definitionsPath));
        var definition = Definition("EO", "睁眼", "#2563EB", null);
        await definitionService.SaveAsync(definition, CancellationToken.None);
        var firstService = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), definitionService);
        var created = await firstService.CreateAsync(definition.Id, 100, 0, EventSource.ManualButton, "review", null, "initial", CancellationToken.None);

        // Recreate both services to exercise JSON persistence, not an in-memory cache.
        var restartedDefinitions = new EventDefinitionService(new EventDefinitionStore(definitionsPath));
        var restarted = new RecordingEventService("recording-1", new RecordingEventStore(temp.Path), restartedDefinitions);
        var loaded = Assert.Single(await restarted.QueryAsync(new RecordingEventQuery("recording-1"), CancellationToken.None));
        await restarted.UpdateAsync(loaded with { StartSample = 250, DurationSamples = 75, Note = "reviewed" }, CancellationToken.None);

        var updated = Assert.Single(await restarted.QueryAsync(new RecordingEventQuery("recording-1"), CancellationToken.None));
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(250, updated.StartSample);
        Assert.Equal(75, updated.DurationSamples);
        Assert.Equal("reviewed", updated.Note);
        Assert.Equal(created.DefinitionSnapshot, updated.DefinitionSnapshot);
    }

    [Fact]
    public async Task Corrupt_event_file_is_not_overwritten_during_a_failed_load()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var path = Path.Combine(temp.Path, "events.json");
        await File.WriteAllTextAsync(path, "{ invalid json", CancellationToken.None);
        var store = new RecordingEventStore(temp.Path);

        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync(CancellationToken.None));
        Assert.Equal("{ invalid json", await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task Event_write_failure_does_not_prevent_the_independent_raw_writer_from_completing()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var definitionService = new EventDefinitionService(new EventDefinitionStore(Path.Combine(temp.Path, "definitions.json")));
        var definition = Definition("EO", "睁眼", "#2563EB", null);
        await definitionService.SaveAsync(definition, CancellationToken.None);
        var rawRoot = Path.Combine(temp.Path, "raw");
        var metadata = new AcquisitionStreamMetadata(
            "test-device", "test device", 500,
            [new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V"),
             new AcquisitionChannel(1, 31, "Counter", AcquisitionChannelKind.SampleCounter, "count")],
            1, DateTimeOffset.UtcNow);
        await using var writer = await new LocalAcquisitionRawWriterFactory().CreateAsync(
            Guid.NewGuid(), metadata, new AcquisitionProjectContext("project", "P001", "Test", rawRoot, "{}"), rawRoot, CancellationToken.None);
        var events = new RecordingEventService("recording-1", new RecordingEventStore(writer.RecordingDirectory), definitionService);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => events.CreateAsync(
            definition.Id, 0, 0, EventSource.ManualButton, "test", null, null, new CancellationToken(canceled: true)));
        await writer.AppendBatchAsync(new AcquisitionBatch(0, 2, 2, [1e-6, 0, 2e-6, 1], DateTimeOffset.UtcNow), CancellationToken.None);
        await writer.CompleteAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(writer.RecordingDirectory, "samples-000001.bin")));
        Assert.Contains("completed", await File.ReadAllTextAsync(Path.Combine(writer.RecordingDirectory, "audit.jsonl"), CancellationToken.None));
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

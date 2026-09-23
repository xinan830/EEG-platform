namespace BrainPlatform.Desktop.Modules.Events.Services;

public sealed class RecordingEventService
{
    private readonly string recordingId;
    private readonly RecordingEventStore store;
    private readonly EventDefinitionService definitions;

    public RecordingEventService(string recordingId, RecordingEventStore store, EventDefinitionService definitions)
    {
        if (string.IsNullOrWhiteSpace(recordingId)) throw new ArgumentException("Recording id is required.", nameof(recordingId));
        this.recordingId = recordingId;
        this.store = store;
        this.definitions = definitions;
    }

    public async Task<RecordingEvent> CreateAsync(
        string definitionId,
        long startSample,
        long durationSamples,
        EventSource source,
        string? sourceDetail,
        string? externalCode,
        string? note,
        CancellationToken cancellationToken)
    {
        if (startSample < 0) throw new EventValidationException("start_sample_invalid", "事件起始采样点不能为负数。");
        if (durationSamples < 0) throw new EventValidationException("duration_invalid", "事件持续采样数不能为负数。");
        var definition = (await definitions.ListAsync(cancellationToken)).FirstOrDefault(item => item.Id == definitionId)
            ?? throw new EventValidationException("definition_not_found", "事件定义不存在。");
        if (!definition.IsEnabled && source is (EventSource.ManualButton or EventSource.KeyboardShortcut))
            throw new EventValidationException("definition_disabled", "停用事件不能用于新建人工标记。");

        var now = DateTimeOffset.UtcNow;
        var item = new RecordingEvent(
            Guid.NewGuid().ToString("N"), recordingId, definition.Id,
            new EventDefinitionSnapshot(definition.Code, definition.Name, definition.Color, definition.Version),
            source, sourceDetail, externalCode, startSample, durationSamples, now, now, note);
        await store.UpsertAsync(item, cancellationToken);
        return item;
    }

    public async Task<IReadOnlyList<RecordingEvent>> QueryAsync(RecordingEventQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        var items = await store.LoadAsync(cancellationToken);
        return items.Where(item =>
                (query.RecordingId is null || item.RecordingId == query.RecordingId) &&
                (query.DefinitionId is null || item.DefinitionId == query.DefinitionId) &&
                (query.DefinitionCode is null || string.Equals(item.DefinitionSnapshot.Code, query.DefinitionCode, StringComparison.OrdinalIgnoreCase)) &&
                (query.Source is null || item.Source == query.Source) &&
                (!query.StartSample.HasValue || item.EndSampleExclusive >= query.StartSample.Value) &&
                (!query.EndSampleExclusive.HasValue || item.StartSample < query.EndSampleExclusive.Value))
            .OrderBy(item => item.StartSample)
            .ToArray();
    }

    public async Task UpdateAsync(RecordingEvent item, CancellationToken cancellationToken)
    {
        if (item.RecordingId != recordingId) throw new EventValidationException("recording_mismatch", "事件不属于当前 Recording。");
        var existing = (await store.LoadAsync(cancellationToken)).FirstOrDefault(value => value.Id == item.Id)
            ?? throw new KeyNotFoundException("未找到要修改的记录事件。");
        if (existing.Source is EventSource.DeviceTrigger or EventSource.ImportedAnnotation or EventSource.Algorithm or EventSource.System)
            throw new EventValidationException("event_read_only", "外部或系统事件不可直接修改。");
        await store.UpsertAsync(item with { UpdatedAtUtc = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public async Task DeleteAsync(string eventId, CancellationToken cancellationToken)
    {
        var item = (await store.LoadAsync(cancellationToken)).FirstOrDefault(value => value.Id == eventId)
            ?? throw new KeyNotFoundException("未找到要删除的记录事件。");
        if (item.Source is EventSource.DeviceTrigger or EventSource.ImportedAnnotation or EventSource.Algorithm or EventSource.System)
            throw new EventValidationException("event_read_only", "外部或系统事件不可直接删除。");
        await store.DeleteAsync(eventId, cancellationToken);
    }
}

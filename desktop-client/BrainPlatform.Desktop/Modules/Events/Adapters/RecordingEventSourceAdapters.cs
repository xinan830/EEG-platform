namespace BrainPlatform.Desktop.Modules.Events.Adapters;

/// <summary>
/// Input received from a device trigger channel after the acquisition layer has
/// resolved its position into the active Recording-relative sample coordinate.
/// The adapter never changes the raw Trigger channel or EEG samples.
/// </summary>
public sealed record DeviceTriggerEventInput(
    string DefinitionId,
    string ExternalCode,
    long StartSample,
    long DurationSamples = 0,
    string? SourceDetail = null,
    string? Note = null);

/// <summary>
/// Input received from a file annotation importer. Start and duration remain in
/// Recording-relative samples, not wall-clock timestamps.
/// </summary>
public sealed record ImportedAnnotationEventInput(
    string DefinitionId,
    string ExternalCode,
    long StartSample,
    long DurationSamples = 0,
    string? SourceDetail = null,
    string? Note = null);

/// <summary>
/// The only translation boundary for physical device triggers. Device code
/// mapping belongs to the caller; this adapter records its provenance as an
/// immutable external event.
/// </summary>
public sealed class DeviceTriggerEventAdapter(RecordingEventService events)
{
    public Task<RecordingEvent> RecordAsync(DeviceTriggerEventInput input, CancellationToken cancellationToken) =>
        events.CreateAsync(
            input.DefinitionId,
            input.StartSample,
            input.DurationSamples,
            EventSource.DeviceTrigger,
            input.SourceDetail ?? "device-trigger",
            input.ExternalCode,
            input.Note,
            cancellationToken);
}

/// <summary>
/// The equivalent boundary for imported annotations. It deliberately shares
/// the RecordingEvent service with manual acquisition and review markers.
/// </summary>
public sealed class ImportedAnnotationEventAdapter(RecordingEventService events)
{
    public Task<RecordingEvent> RecordAsync(ImportedAnnotationEventInput input, CancellationToken cancellationToken) =>
        events.CreateAsync(
            input.DefinitionId,
            input.StartSample,
            input.DurationSamples,
            EventSource.ImportedAnnotation,
            input.SourceDetail ?? "imported-annotation",
            input.ExternalCode,
            input.Note,
            cancellationToken);
}

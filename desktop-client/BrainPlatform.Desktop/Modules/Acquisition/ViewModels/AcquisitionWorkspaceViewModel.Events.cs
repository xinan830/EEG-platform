using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Events.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class AcquisitionWorkspaceViewModel
{
    internal const string PendingIntervalEventId = AcquisitionRecordingEventController.PendingIntervalEventId;

    public ObservableCollection<EventDefinition> EnabledEventDefinitions => recordingEvents.EnabledEventDefinitions;

    /// <summary>
    /// Events already durably written for the active recording. The live canvas
    /// reads this in-memory projection; it never polls the event file on a
    /// render tick.
    /// </summary>
    public ObservableCollection<RecordingEvent> LiveRecordingEvents => recordingEvents.LiveRecordingEvents;

    public ObservableCollection<EventGridTileItem> EventGridTiles => recordingEvents.EventGridTiles;

    public string? SelectedEventDefinitionId
    {
        get => recordingEvents.SelectedEventDefinitionId;
        set => recordingEvents.SelectedEventDefinitionId = value;
    }

    public bool HasActiveInterval => recordingEvents.HasActiveInterval;

    public string ActiveIntervalStatusText => recordingEvents.ActiveIntervalStatusText;

    public void SyncEventGridTiles() => recordingEvents.SyncEventGridTiles();

    public Task TriggerEventFromGridAsync(
        EventDefinition definition,
        string? note = null,
        EventSource source = EventSource.ManualButton,
        string? sourceDetail = "acquisition-grid",
        CancellationToken cancellationToken = default) =>
        recordingEvents.TriggerEventFromGridAsync(definition, note, source, sourceDetail, cancellationToken);

    public void CancelActiveInterval() => recordingEvents.CancelActiveInterval();

    public Task<RecordingEvent> MarkEventAsync(
        string definitionId,
        EventSource source = EventSource.ManualButton,
        string? sourceDetail = null,
        CancellationToken cancellationToken = default) =>
        recordingEvents.MarkEventAsync(definitionId, source, sourceDetail, cancellationToken);

    public Task RefreshEventDefinitionsAsync() => recordingEvents.RefreshDefinitionsAsync();

    private Task MarkSelectedEventAsync() => recordingEvents.MarkSelectedEventAsync();
}

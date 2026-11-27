using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Events.Services;
using BrainPlatform.Desktop.Modules.Events.ViewModels;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

internal sealed record AcquisitionEventFeedback(string Message, bool IsSuccess);

/// <summary>
/// Sole owner of live acquisition event definitions, grid tiles, active interval state,
/// in-memory event projection, and recording-relative sample coordinate capture/persistence.
/// </summary>
internal sealed class AcquisitionRecordingEventController : ObservableObject, IDisposable
{
    internal const string PendingIntervalEventId = "__pending_interval__";

    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly EventDefinitionService eventDefinitionService;
    private string? selectedEventDefinitionId;
    private EventDefinition? activeIntervalDefinition;
    private RecordingEventCoordinate? activeIntervalStartCoordinate;
    private string activeIntervalStartClockText = string.Empty;
    private long refreshGeneration;
    private bool disposed;

    public AcquisitionRecordingEventController(
        ConfiguredAcquisitionRuntime runtime,
        EventDefinitionService eventDefinitionService)
    {
        this.runtime = runtime;
        this.eventDefinitionService = eventDefinitionService;
    }

    public event EventHandler<AcquisitionEventFeedback>? FeedbackRaised;

    public ObservableCollection<EventDefinition> EnabledEventDefinitions { get; } = [];

    public ObservableCollection<RecordingEvent> LiveRecordingEvents { get; } = [];

    public ObservableCollection<EventGridTileItem> EventGridTiles { get; } = [];

    public string? SelectedEventDefinitionId
    {
        get => selectedEventDefinitionId;
        set => SetProperty(ref selectedEventDefinitionId, value);
    }

    public bool HasActiveInterval => activeIntervalDefinition is not null;

    public string ActiveIntervalStatusText => activeIntervalDefinition is null
        ? string.Empty
        : $"进行中：{activeIntervalDefinition.Name}（{activeIntervalDefinition.EffectiveStartLabel} @ {activeIntervalStartClockText}）· 再次点击结束“{activeIntervalDefinition.EffectiveEndLabel}”";

    public async Task<RecordingEvent> MarkEventAsync(
        string definitionId,
        EventSource source = EventSource.ManualButton,
        string? sourceDetail = null,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State.State != AcquisitionState.Recording)
        {
            throw new InvalidOperationException("只有正在记录时才能添加事件标记。");
        }

        // Capture the coordinate before scheduling or awaiting persistence.
        var coordinate = CaptureCurrentEventCoordinate();
        return await PersistEventAtCoordinateAsync(
            definitionId,
            coordinate,
            durationSamples: 0,
            source,
            sourceDetail,
            note: null,
            cancellationToken);
    }

    public async Task MarkSelectedEventAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedEventDefinitionId))
        {
            throw new InvalidOperationException("请先选择一个事件定义。");
        }

        await MarkEventAsync(SelectedEventDefinitionId, EventSource.ManualButton, "acquisition-toolbar", cancellationToken);
    }

    public async Task TriggerEventFromGridAsync(
        EventDefinition definition,
        string? note = null,
        EventSource source = EventSource.ManualButton,
        string? sourceDetail = "acquisition-grid",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (runtime.State.State != AcquisitionState.Recording)
        {
            throw new InvalidOperationException("只有正在记录时才能添加事件标记。");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!definition.IsIntervalKind)
        {
            await PersistEventAtCoordinateAsync(
                definition.Id,
                CaptureCurrentEventCoordinate(),
                durationSamples: 0,
                source,
                sourceDetail,
                trimmedNote,
                cancellationToken);
            return;
        }

        if (activeIntervalDefinition is not null
            && string.Equals(activeIntervalDefinition.Id, definition.Id, StringComparison.Ordinal)
            && activeIntervalStartCoordinate is { } startCoordinate)
        {
            var endCoordinate = CaptureCurrentEventCoordinate();
            var durationSamples = Math.Max(1L, endCoordinate.RecordingRelativeSample - startCoordinate.RecordingRelativeSample);
            CancelActiveInterval();
            await PersistEventAtCoordinateAsync(
                definition.Id,
                startCoordinate,
                durationSamples,
                source,
                sourceDetail,
                trimmedNote,
                cancellationToken);
            return;
        }

        var start = CaptureCurrentEventCoordinate();
        activeIntervalDefinition = definition;
        activeIntervalStartCoordinate = start;
        activeIntervalStartClockText = FormatCoordinateClockText(start);
        UpsertPendingIntervalMarker(definition, start, source, sourceDetail, trimmedNote);
        UpdateTileActiveStates();
        RaisePropertyChanged(nameof(HasActiveInterval));
        RaisePropertyChanged(nameof(ActiveIntervalStatusText));

        var startMessage = $"已开始“{definition.Name}（{definition.EffectiveStartLabel}）” @ {activeIntervalStartClockText}，再次点击结束“{definition.EffectiveEndLabel}”。";
        FeedbackRaised?.Invoke(this, new AcquisitionEventFeedback(startMessage, IsSuccess: false));
    }

    public async Task RefreshDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref refreshGeneration);
        var definitions = await eventDefinitionService.ListAsync(cancellationToken);
        if (disposed || Volatile.Read(ref refreshGeneration) != generation)
        {
            return;
        }

        EnabledEventDefinitions.Clear();
        foreach (var definition in definitions.Where(item => item.IsEnabled).OrderBy(item => item.Name, StringComparer.CurrentCulture))
        {
            EnabledEventDefinitions.Add(definition);
        }

        SelectedEventDefinitionId ??= EnabledEventDefinitions.FirstOrDefault()?.Id;
        SyncEventGridTiles();
        RaisePropertyChanged(nameof(EnabledEventDefinitions));
    }

    public void SyncEventGridTiles()
    {
        var activeId = activeIntervalDefinition?.Id;
        EventGridTiles.Clear();
        var activeStillPresent = false;
        foreach (var definition in EnabledEventDefinitions)
        {
            var tile = new EventGridTileItem(definition);
            if (activeId is not null && string.Equals(definition.Id, activeId, StringComparison.Ordinal))
            {
                tile.IsActiveInterval = true;
                tile.ActivePromptText = $"点击结束：{definition.EffectiveEndLabel}";
                activeStillPresent = true;
            }

            EventGridTiles.Add(tile);
        }

        if (activeId is not null && !activeStillPresent)
        {
            CancelActiveInterval();
        }
    }

    public void CancelActiveInterval()
    {
        RemovePendingIntervalMarker();
        if (activeIntervalDefinition is null && activeIntervalStartCoordinate is null)
        {
            return;
        }

        activeIntervalDefinition = null;
        activeIntervalStartCoordinate = null;
        activeIntervalStartClockText = string.Empty;
        UpdateTileActiveStates();
        RaisePropertyChanged(nameof(HasActiveInterval));
        RaisePropertyChanged(nameof(ActiveIntervalStatusText));
    }

    public void ClearLiveEvents()
    {
        CancelActiveInterval();
        LiveRecordingEvents.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Increment(ref refreshGeneration);
        CancelActiveInterval();
    }

    private RecordingEventCoordinate CaptureCurrentEventCoordinate()
    {
        var latestSample = runtime.LatestSampleCounter
            ?? throw new InvalidOperationException("当前还没有可用的采样坐标。");
        var recordingFirstSample = runtime.RecordingFirstSampleCounter
            ?? throw new InvalidOperationException("当前 Recording 尚未建立首采样坐标。");
        var coordinate = RecordingEventCoordinateResolver.Resolve(
            latestSample,
            recordingFirstSample,
            runtime.GetRecordingGaps());
        if (coordinate.Status == EventCoordinateStatus.UnavailableDiscontinuity)
        {
            throw new InvalidOperationException("设备采样计数器已重置，无法为当前 Recording 生成可追溯事件坐标。");
        }

        return coordinate;
    }

    private async Task<RecordingEvent> PersistEventAtCoordinateAsync(
        string definitionId,
        RecordingEventCoordinate coordinate,
        long durationSamples,
        EventSource source,
        string? sourceDetail,
        string? note,
        CancellationToken cancellationToken)
    {
        var recordingId = runtime.RecordingSessionId?.ToString("N")
            ?? throw new InvalidOperationException("当前没有活动 Recording。");
        var directory = runtime.RecordingDirectory
            ?? throw new InvalidOperationException("当前 Recording 目录不可用。");
        var service = new RecordingEventService(
            recordingId,
            new RecordingEventStore(directory),
            eventDefinitionService);
        var item = await service.CreateAsync(
            definitionId,
            coordinate.RecordingRelativeSample,
            durationSamples,
            source,
            sourceDetail,
            null,
            note,
            cancellationToken,
            sourceSampleCounter: coordinate.SourceSampleCounter,
            coordinateStatus: coordinate.Status,
            coordinateUnavailableReason: coordinate.UnavailableReason);
        LiveRecordingEvents.Add(item);

        var markerTime = FormatCoordinateClockText(coordinate);
        var message = item.IsInterval
            ? $"持续事件“{item.DefinitionSnapshot.Name}（{item.DefinitionSnapshot.EffectiveStartLabel} → {item.DefinitionSnapshot.EffectiveEndLabel}）”已记录，起始 {markerTime}。"
            : $"事件“{item.DefinitionSnapshot.Name}”已标记在记录时间 {markerTime}。";
        FeedbackRaised?.Invoke(this, new AcquisitionEventFeedback(message, IsSuccess: true));
        return item;
    }

    private void UpsertPendingIntervalMarker(
        EventDefinition definition,
        RecordingEventCoordinate startCoordinate,
        EventSource source,
        string? sourceDetail,
        string? note)
    {
        RemovePendingIntervalMarker();
        var recordingId = runtime.RecordingSessionId?.ToString("N") ?? "live";
        var now = DateTimeOffset.UtcNow;
        var pending = new RecordingEvent(
            Id: PendingIntervalEventId,
            RecordingId: recordingId,
            DefinitionId: definition.Id,
            DefinitionSnapshot: new EventDefinitionSnapshot(
                definition.Code,
                definition.Name,
                definition.Color,
                definition.Version,
                definition.Kind,
                definition.StartLabel,
                definition.EndLabel,
                definition.IconPath),
            Source: source,
            SourceDetail: sourceDetail,
            ExternalCode: null,
            StartSample: startCoordinate.RecordingRelativeSample,
            DurationSamples: 0,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            Note: note,
            SourceSampleCounter: startCoordinate.SourceSampleCounter,
            CoordinateStatus: startCoordinate.Status,
            CoordinateUnavailableReason: startCoordinate.UnavailableReason);
        LiveRecordingEvents.Add(pending);
    }

    private void RemovePendingIntervalMarker()
    {
        for (var i = LiveRecordingEvents.Count - 1; i >= 0; i--)
        {
            if (string.Equals(LiveRecordingEvents[i].Id, PendingIntervalEventId, StringComparison.Ordinal))
            {
                LiveRecordingEvents.RemoveAt(i);
            }
        }
    }

    private void UpdateTileActiveStates()
    {
        var activeId = activeIntervalDefinition?.Id;
        foreach (var tile in EventGridTiles)
        {
            var isActive = activeId is not null && string.Equals(tile.Id, activeId, StringComparison.Ordinal);
            tile.IsActiveInterval = isActive;
            tile.ActivePromptText = isActive ? $"点击结束：{tile.Definition.EffectiveEndLabel}" : null;
        }
    }

    private string FormatCoordinateClockText(RecordingEventCoordinate coordinate)
    {
        var recordingStartUtc = runtime.RecordingStartUtc;
        var samplingRateHz = runtime.StreamMetadata?.SamplingRateHz;
        return recordingStartUtc is { } start && samplingRateHz is > 0
            ? RecordingEventDisplayTime.FormatClockTime(start, coordinate.RecordingRelativeSample, samplingRateHz.Value)
            : $"第 {coordinate.RecordingRelativeSample} 个采样点";
    }
}

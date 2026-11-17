using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel
{
    private string? selectedEventDefinitionId;
    private string editEventNote = string.Empty;
    private string addEventTimeText = "00:00:00.000";
    private string editEventTimeText = "00:00:00.000";
    private bool isEventEditorOpen;
    private bool isEventSaving;
    private string eventErrorText = string.Empty;
    private RecordingEvent? selectedRecordingEvent;

    public ObservableCollection<RecordingEvent> RecordingEvents { get; } = [];
    public ObservableCollection<RecordingEventOption> RecordingEventOptions { get; } = [];
    public ObservableCollection<EventDefinition> EnabledEventDefinitions { get; } = [];
    public IReadOnlyList<RecordingLifecycleBoundary> LifecycleBoundaries => reviewReader.LifecycleBoundaries;
    public EventDefinitionService? EventDefinitionService { get; }
    public ICommand MarkSelectedEventCommand { get; private set; } = null!;
    public ICommand DeleteSelectedEventCommand { get; private set; } = null!;
    public ICommand SaveEditedEventCommand { get; private set; } = null!;
    public ICommand OpenEventEditorCommand { get; private set; } = null!;
    public ICommand CloseEventEditorCommand { get; private set; } = null!;
    public ICommand UseCurrentEventTimeCommand { get; private set; } = null!;

    private void InitializeEventCommands()
    {
        MarkSelectedEventCommand = new AsyncRelayCommand(AddEventAtEnteredTimeAsync, ReportEventError);
        DeleteSelectedEventCommand = new AsyncRelayCommand(DeleteSelectedEventAsync, ReportEventError);
        SaveEditedEventCommand = new AsyncRelayCommand(SaveEditedEventAsync, ReportEventError);
        OpenEventEditorCommand = new RelayCommand(() =>
        {
            if (IsEventEditorOpen) { CancelEventPlacement(); IsEventEditorOpen = false; }
            else IsEventEditorOpen = true;
        }, () => CanEditSelectedEvent);
        CloseEventEditorCommand = new RelayCommand(() => { CancelEventPlacement(); IsEventEditorOpen = false; });
        UseCurrentEventTimeCommand = new RelayCommand(() => AddEventTimeText = ReviewEventTime.Format(PositionSeconds));
        InitializeEventPlacementCommands();
    }

    public string? SelectedEventDefinitionId
    {
        get => selectedEventDefinitionId;
        set { if (SetProperty(ref selectedEventDefinitionId, value)) CancelEventPlacement(); }
    }
    public string AddEventTimeText { get => addEventTimeText; set => SetProperty(ref addEventTimeText, value); }
    public string EditEventTimeText
    {
        get => editEventTimeText;
        set { if (SetProperty(ref editEventTimeText, value)) RaisePropertyChanged(nameof(EditEventClockText)); }
    }
    public string EditEventNote { get => editEventNote; set => SetProperty(ref editEventNote, value); }
    public double EditEventStartSeconds => SelectedRecordingEvent?.StartSample / (double)samplingRateHz ?? 0;
    public string EditEventClockText
    {
        get
        {
            try { return RecordingEventDisplayTime.FormatClock(recordingStartUtc,
                EventSampleFromText(EditEventTimeText, SelectedRecordingEvent), samplingRateHz); }
            catch (EventValidationException) { return "时间无效"; }
        }
    }
    public bool IsEventEditorOpen { get => isEventEditorOpen; set => SetProperty(ref isEventEditorOpen, value); }
    public bool IsEventSaving { get => isEventSaving; private set => SetProperty(ref isEventSaving, value); }
    public string EventErrorText { get => eventErrorText; private set => SetProperty(ref eventErrorText, value); }
    public RecordingEvent? SelectedRecordingEvent
    {
        get => selectedRecordingEvent;
        set
        {
            if (!SetProperty(ref selectedRecordingEvent, value)) return;
            CancelEventPlacement();
            RaisePropertyChanged(nameof(CanEditSelectedEvent));
            (OpenEventEditorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            EditEventTimeText = ReviewEventTime.Format(value?.StartSample / (double)samplingRateHz ?? 0);
            EditEventNote = value?.Note ?? string.Empty;
            RaisePropertyChanged(nameof(EditEventClockText));
        }
    }

    private long EventSampleCount => playback.DurationSamples;
    private long EventSampleFromText(string text, RecordingEvent? original = null)
    {
        // Unchanged millisecond text must not quantize an existing sub-millisecond sample.
        if (original is not null && text == ReviewEventTime.Format(original.StartSample / (double)samplingRateHz))
            return original.StartSample;
        return ReviewEventTime.ToSample(ReviewEventTime.Parse(text), samplingRateHz, EventSampleCount, original?.DurationSamples ?? 0);
    }

    public async Task LoadEventsAsync()
    {
        if (eventStore is null) return;
        var selectedId = SelectedRecordingEvent?.Id;
        var events = await eventStore.LoadAsync(CancellationToken.None);
        SelectedRecordingEvent = null;
        RecordingEvents.Clear();
        RecordingEventOptions.Clear();
        foreach (var item in events.OrderBy(item => item.StartSample))
        {
            RecordingEvents.Add(item);
            RecordingEventOptions.Add(new RecordingEventOption(item,
                $"{TimeDisplay.FormatPoint(item.StartSample / (double)samplingRateHz)} {item.DefinitionSnapshot.Name}",
                RecordingEventDisplayTime.FormatMarker(item, recordingStartUtc, samplingRateHz)));
        }
        SelectedRecordingEvent = RecordingEvents.FirstOrDefault(item => item.Id == selectedId);
        RaisePropertyChanged(nameof(RecordingEvents));
        RaisePropertyChanged(nameof(CanEditSelectedEvent));
    }

    private async Task LoadEnabledDefinitionsAsync()
    {
        EnabledEventDefinitions.Clear();
        if (EventDefinitionService is null) return;
        foreach (var definition in (await EventDefinitionService.ListAsync(CancellationToken.None)).Where(item => item.IsEnabled))
            EnabledEventDefinitions.Add(definition);
        SelectedEventDefinitionId ??= EnabledEventDefinitions.FirstOrDefault()?.Id;
        AddEventTimeText = ReviewEventTime.Format(PositionSeconds);
    }

    public Task CreateManualEventAsync(string definitionId, string? note = null) =>
        CreateEventAtSampleAsync(definitionId, EventSampleFromSeconds(PositionSeconds), EventSource.ManualButton, "review-toolbar", note);
    public Task CreateKeyboardEventAsync(string definitionId, string shortcut) =>
        CreateEventAtSampleAsync(definitionId, EventSampleFromSeconds(PositionSeconds), EventSource.KeyboardShortcut, shortcut);
    public void ReportEventShortcutError(Exception exception) => ReportEventError(exception);

    public Task AddEventAtEnteredTimeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedEventDefinitionId))
            throw new EventValidationException("definition_required", "请先选择事件类型。");
        return CreateEventAtSampleAsync(SelectedEventDefinitionId, EventSampleFromText(AddEventTimeText),
            EventSource.ManualButton, "review-time-input");
    }
    private long EventSampleFromSeconds(double seconds, long durationSamples = 0) =>
        ReviewEventTime.ToSample(seconds, samplingRateHz, EventSampleCount, durationSamples);

    private async Task WriteEventAsync(Func<RecordingEventService, Task> write)
    {
        if (isDisposing || eventStore is null || EventDefinitionService is null)
            throw new EventValidationException("event_edit_unavailable", "当前回溯记录不支持事件编辑。");
        if (IsEventSaving) throw new EventValidationException("event_busy", "事件正在保存，请稍后再试。");
        IsEventSaving = true;
        EventErrorText = string.Empty;
        try { await write(new RecordingEventService(recordingId, eventStore, EventDefinitionService)); }
        catch (Exception exception) { ReportEventError(exception); throw; }
        finally { IsEventSaving = false; }
    }

    private Task CreateEventAtSampleAsync(string definitionId, long sample, EventSource source, string detail, string? note = null) =>
        WriteEventAsync(async service =>
        {
            var created = await service.CreateAsync(definitionId, sample, 0, source, detail, null, note, CancellationToken.None);
            await LoadEventsAsync();
            SelectedRecordingEvent = RecordingEvents.FirstOrDefault(item => item.Id == created.Id);
            notifications?.PublishSuccess("事件已标记。");
        });

    public Task DeleteSelectedEventAsync()
    {
        if (!CanEditSelectedEvent || SelectedRecordingEvent is not { } item)
            throw new EventValidationException("event_read_only", "只能删除人工创建的事件标记。");
        return WriteEventAsync(async service =>
        {
            await service.DeleteAsync(item.Id, CancellationToken.None);
            await LoadEventsAsync();
            notifications?.PublishSuccess("事件已删除。");
        });
    }

    public async Task SaveEditedEventAsync()
    {
        if (SelectedRecordingEvent is not { } item)
            throw new EventValidationException("event_not_selected", "请先选择事件。");
        await MoveEventToSampleAsync(item, EventSampleFromText(EditEventTimeText, item), EventNoteForSave(item));
        IsEventEditorOpen = false;
    }
    private string? EventNoteForSave(RecordingEvent item) => EditEventNote == (item.Note ?? string.Empty)
        ? item.Note : string.IsNullOrWhiteSpace(EditEventNote) ? null : EditEventNote.Trim();

    private Task MoveEventToSampleAsync(RecordingEvent item, long sample, string? note)
    {
        if (item.Source is not (EventSource.ManualButton or EventSource.KeyboardShortcut))
            throw new EventValidationException("event_read_only", "外部或系统事件不可直接修改。");
        if (!item.IsDisplayable)
            throw new EventValidationException("event_coordinate_unavailable", "该事件没有可用的波形坐标。");
        if (sample < 0 || sample >= EventSampleCount || item.DurationSamples > EventSampleCount - sample)
            throw new EventValidationException("event_duration_invalid", "事件移动后超出录音范围。");
        return WriteEventAsync(async service =>
        {
            await service.UpdateAsync(item with { StartSample = sample, Note = note }, CancellationToken.None);
            await LoadEventsAsync();
            SelectedRecordingEvent = RecordingEvents.FirstOrDefault(value => value.Id == item.Id);
            notifications?.PublishSuccess("事件已更新。");
        });
    }

    private void ReportEventError(Exception exception)
    {
        EventErrorText = exception.Message;
        notifications?.PublishError(exception.Message);
    }
    private void ReportCommandError(Exception exception) => notifications?.PublishError(exception.Message);
    public Task SeekToEventAsync(RecordingEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsDisplayable)
            throw new EventValidationException("event_coordinate_unavailable", item.CoordinateUnavailableReason ?? "该事件没有可用的波形坐标。");
        return SeekAsync(RecordingTimeMapper.ToElapsedSeconds(item.StartSample, samplingRateHz));
    }
}

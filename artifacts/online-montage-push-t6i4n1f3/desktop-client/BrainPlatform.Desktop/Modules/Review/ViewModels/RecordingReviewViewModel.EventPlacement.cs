using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public enum ReviewEventPlacementMode { None, Add, Move }

public sealed partial class RecordingReviewViewModel
{
    private ReviewEventPlacementMode eventPlacementMode;
    private long? eventPreviewSample;
    private RecordingEvent? placementEvent;
    private string? placementDefinitionId;
    private string eventPlacementColor = "#2563EB";
    public ReviewEventPlacementMode EventPlacementMode => eventPlacementMode;
    public bool IsEventPlacementActive => eventPlacementMode != ReviewEventPlacementMode.None;
    public long? EventPreviewSample => eventPreviewSample;
    public string EventPlacementColor => eventPlacementColor;
    public string EventPlacementHint => eventPlacementMode switch
    {
        ReviewEventPlacementMode.Add => "在波形上点击添加事件 · Esc 取消",
        ReviewEventPlacementMode.Move => "点击新位置或拖动事件线 · Esc 取消",
        _ => string.Empty,
    };
    public string EventPreviewTimeText => eventPreviewSample is { } sample
        ? ReviewEventTime.Format(sample / (double)samplingRateHz) : string.Empty;
    public ICommand BeginWaveformAddCommand { get; private set; } = null!;
    public ICommand BeginWaveformMoveCommand { get; private set; } = null!;
    public ICommand CancelEventPlacementCommand { get; private set; } = null!;

    private void InitializeEventPlacementCommands()
    {
        BeginWaveformAddCommand = new RelayCommand(() =>
        {
            try { BeginEventPlacement(false); } catch (Exception exception) { ReportEventError(exception); }
        });
        BeginWaveformMoveCommand = new RelayCommand(() =>
        {
            try { BeginEventPlacement(true); } catch (Exception exception) { ReportEventError(exception); }
        });
        CancelEventPlacementCommand = new RelayCommand(CancelEventPlacement);
    }

    /// <summary>Pauses playback and captures a fixed event/type identity for one placement.</summary>
    public void BeginEventPlacement(bool move, RecordingEvent? draggedEvent = null)
    {
        if (IsEventSaving || isDisposing) throw new EventValidationException("event_busy", "事件正在保存或页面已关闭。");
        var item = move ? draggedEvent ?? SelectedRecordingEvent : null;
        var definition = EnabledEventDefinitions.FirstOrDefault(value => value.Id == SelectedEventDefinitionId);
        if (move && (item is null || item.Source is not (EventSource.ManualButton or EventSource.KeyboardShortcut)))
            throw new EventValidationException("event_read_only", "请选择可编辑的人工事件。");
        if (move && item is { IsDisplayable: false })
            throw new EventValidationException("event_coordinate_unavailable", "该事件没有可用的波形坐标。");
        if (!move && (definition is null || !definition.IsEnabled))
            throw new EventValidationException("definition_required", "请先选择可用的事件类型。");
        if (IsPlaying) TogglePlayback();
        if (move) SelectedRecordingEvent = item;
        placementEvent = item;
        placementDefinitionId = definition?.Id;
        eventPlacementColor = move ? item!.DefinitionSnapshot.Color : definition!.Color;
        eventPlacementMode = move ? ReviewEventPlacementMode.Move : ReviewEventPlacementMode.Add;
        EventErrorText = string.Empty;
        SetEventPreviewSample(item?.StartSample);
        PublishEventPlacement();
    }

    /// <summary>Updates UI only; invalid or out-of-range targets have no preview and cannot commit.</summary>
    public bool PreviewEventAtSeconds(double seconds)
    {
        if (!IsEventPlacementActive || IsEventSaving) return false;
        try
        {
            SetEventPreviewSample(EventSampleFromSeconds(seconds, placementEvent?.DurationSamples ?? 0));
            EventErrorText = string.Empty;
            return true;
        }
        catch (EventValidationException exception)
        {
            EventErrorText = exception.Message;
            SetEventPreviewSample(null);
            return false;
        }
    }

    public void HideEventPreview() => SetEventPreviewSample(null);

    public async Task CommitEventPlacementAsync()
    {
        if (!IsEventPlacementActive || eventPreviewSample is not { } sample || IsEventSaving) return;
        var mode = eventPlacementMode;
        var item = placementEvent;
        var definitionId = placementDefinitionId;
        var note = item is not null && IsEventEditorOpen && SelectedRecordingEvent?.Id == item.Id
            ? EventNoteForSave(item) : item?.Note;
        // Persistence may refresh selection; retain the target and draft if the write fails.
        try
        {
            if (mode == ReviewEventPlacementMode.Move && item is not null)
                await MoveEventToSampleAsync(item, sample, note);
            else if (mode == ReviewEventPlacementMode.Add && definitionId is not null)
                await CreateEventAtSampleAsync(definitionId, sample, EventSource.ManualButton, "review-waveform");
            CancelEventPlacement();
        }
        catch (Exception exception) { ReportEventError(exception); throw; }
    }

    public void CancelEventPlacement()
    {
        placementEvent = null;
        placementDefinitionId = null;
        eventPlacementMode = ReviewEventPlacementMode.None;
        SetEventPreviewSample(null);
        PublishEventPlacement();
    }

    private void SetEventPreviewSample(long? sample)
    {
        SetProperty(ref eventPreviewSample, sample, nameof(EventPreviewSample));
        RaisePropertyChanged(nameof(EventPreviewTimeText));
    }
    private void PublishEventPlacement()
    {
        RaisePropertyChanged(nameof(EventPlacementMode));
        RaisePropertyChanged(nameof(IsEventPlacementActive));
        RaisePropertyChanged(nameof(EventPlacementColor));
        RaisePropertyChanged(nameof(EventPlacementHint));
    }
}

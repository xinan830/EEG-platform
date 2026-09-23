using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.Events;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class RecordingReviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RecordingReviewSession session;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly RecordingPlaybackController playback;
    private readonly int samplingRateHz;
    private readonly int sourceChannelCount;
    private readonly string projectName;
    private readonly string recordingName;
    private readonly DateTimeOffset recordingStartUtc;
    private readonly RecordingEventStore? eventStore;
    private readonly string recordingId;
    private readonly OperationNotificationCenter? notifications;
    private Task? prefetchTask;
    private Task? playbackLoadTask;
    private readonly LatestThrottledTask viewportLoader = new(TimeSpan.FromMilliseconds(50));
    private readonly LatestDelayedTask filterUpdater = new();
    private double highPassHz = 1;
    private double lowPassHz = 30;
    private double notchHz = 50;
    private readonly WaveformDisplaySettings displaySettings = new();
    private Task? viewportDurationUpdateTask;
    private bool viewportDurationUpdatePending;
    private double viewportStartSeconds;
    private RecordingReviewFrame? publishedFrame;

    public RecordingReviewViewModel(
        IRecordingReviewReader reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds = 10,
        string? recordingName = null,
        string? projectName = null,
        IRecordingReviewFilter? filter = null,
        Func<double>? playbackClockSeconds = null,
        string? recordingDirectory = null,
        EventDefinitionService? eventDefinitionService = null,
        OperationNotificationCenter? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        recordingId = reader.Manifest.SessionId.ToString("N");
        session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds, filter);
        playback = new RecordingPlaybackController(reader.DurationSeconds, playbackClockSeconds);
        samplingRateHz = reader.Manifest.SamplingRateHz;
        recordingStartUtc = reader.Manifest.RecordingStartUtc;
        sourceChannelCount = reader.Manifest.Channels.Count(channel =>
            channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar);
        this.projectName = string.IsNullOrWhiteSpace(projectName)
            ? reader.Manifest.Project.Name
            : projectName;
        this.recordingName = string.IsNullOrWhiteSpace(recordingName)
            ? "采集记录"
            : recordingName;
        eventStore = recordingDirectory is null ? null : new RecordingEventStore(recordingDirectory);
        EventDefinitionService = eventDefinitionService;
        this.notifications = notifications;
        MarkSelectedEventCommand = new AsyncRelayCommand(MarkSelectedEventAsync, ReportCommandError);
        DeleteSelectedEventCommand = new AsyncRelayCommand(DeleteSelectedEventWithNotificationAsync, ReportCommandError);
        SaveEditedEventCommand = new AsyncRelayCommand(SaveEditedEventAsync, ReportCommandError);
        OpenEventEditorCommand = new RelayCommand(() => IsEventEditorOpen = true, () => CanEditSelectedEvent);
        CloseEventEditorCommand = new RelayCommand(() => IsEventEditorOpen = false);
        displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        session.Changed += OnSessionChanged;
        foreach (var item in catalog.CompatibleViewingMontages)
        {
            CompatibleViewingMontages.Add(item.Profile);
        }
    }

    public ObservableCollection<MontageProfile> CompatibleViewingMontages { get; } = [];

    public ObservableCollection<RecordingEvent> RecordingEvents { get; } = [];
    public ObservableCollection<EventDefinition> EnabledEventDefinitions { get; } = [];

    private string? selectedEventDefinitionId;
    private string editEventNote = string.Empty;
    private double editEventStartSeconds;
    private double editEventDurationSeconds;
    private bool isEventEditorOpen;
    public string? SelectedEventDefinitionId
    {
        get => selectedEventDefinitionId;
        set => SetProperty(ref selectedEventDefinitionId, value);
    }

    public string EditEventNote { get => editEventNote; set => SetProperty(ref editEventNote, value); }
    public double EditEventStartSeconds { get => editEventStartSeconds; set => SetProperty(ref editEventStartSeconds, value); }
    public double EditEventDurationSeconds { get => editEventDurationSeconds; set => SetProperty(ref editEventDurationSeconds, value); }
    public bool IsEventEditorOpen { get => isEventEditorOpen; set => SetProperty(ref isEventEditorOpen, value); }

    private RecordingEvent? selectedRecordingEvent;
    public RecordingEvent? SelectedRecordingEvent
    {
        get => selectedRecordingEvent;
        set
        {
            if (SetProperty(ref selectedRecordingEvent, value))
            {
                RaisePropertyChanged(nameof(CanEditSelectedEvent));
                (OpenEventEditorCommand as RelayCommand)?.RaiseCanExecuteChanged();
                LoadSelectedEventEditor(value);
            }
        }
    }

    public EventDefinitionService? EventDefinitionService { get; }

    public System.Windows.Input.ICommand MarkSelectedEventCommand { get; }

    public System.Windows.Input.ICommand DeleteSelectedEventCommand { get; }

    public System.Windows.Input.ICommand SaveEditedEventCommand { get; }

    public System.Windows.Input.ICommand OpenEventEditorCommand { get; }

    public System.Windows.Input.ICommand CloseEventEditorCommand { get; }

    public bool CanEditSelectedEvent => SelectedRecordingEvent is { Source: EventSource.ManualButton or EventSource.KeyboardShortcut };

    public WaveformDisplaySettings DisplaySettings => displaySettings;

    public string AcquisitionMontageText => catalog.AcquisitionMontageStatusText;

    public MontageProfile? AcquisitionMontage => session.AcquisitionMontage;

    public MontageProfile? CurrentViewingMontage => session.SelectedViewingMontage;

    public RecordingReviewFrame? CurrentFrame => session.CurrentFrame;

    public double PositionSeconds => playback.PositionSeconds;

    /// <summary>The left edge shown by the chart. This changes at render cadence while navigating.</summary>
    public double ViewportStartSeconds => viewportStartSeconds;

    public double DurationSeconds => session.DurationSeconds;

    public int SamplingRateHz => samplingRateHz;

    public int SourceChannelCount => sourceChannelCount;

    public string ProjectName => projectName;

    public string RecordingName => recordingName;

    public DateTimeOffset RecordingStartUtc => recordingStartUtc;

    public double VisibleDurationSeconds => session.VisibleDurationSeconds;

    public int OutputChannelCount => session.CurrentFrame?.OutputChannelNames.Count ?? 0;

    public bool IsLoading => session.IsLoading;

    public bool IsPlaying => playback.IsPlaying;

    public bool IsCompleted => playback.IsCompleted;

    public string PlaybackSpeedText => $"{playback.PlaybackRate:0.0#}X";

    public string StatusText => session.StatusText;

    public ReviewPreparationState PreparationState => session.PreparationState;

    public double? PreparationProgress => PreparationState.Progress;

    public string RecordingStartText => FormatElapsedTime(0);

    public string RecordingEndText => FormatElapsedTime(DurationSeconds);

    public string PositionTimeText => FormatElapsedTime(PositionSeconds);

    public string WindowTimeText
    {
        get
        {
            var end = Math.Min(DurationSeconds, ViewportStartSeconds + VisibleDurationSeconds);
            return $"{FormatElapsedTime(ViewportStartSeconds)} - {FormatElapsedTime(end)}";
        }
    }

    public double HighPassHz
    {
        get => highPassHz;
        set
        {
            if (SetProperty(ref highPassHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double LowPassHz
    {
        get => lowPassHz;
        set
        {
            if (SetProperty(ref lowPassHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double NotchHz
    {
        get => notchHz;
        set
        {
            if (SetProperty(ref notchHz, value))
            {
                ScheduleFilterUpdate();
            }
        }
    }

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => displaySettings.SensitivityMicrovoltsPerMillimeter;
        set => displaySettings.SensitivityMicrovoltsPerMillimeter = value;
    }

    public double PaperSpeedMillimetersPerSecond
    {
        get => displaySettings.PaperSpeedMillimetersPerSecond;
        set => displaySettings.PaperSpeedMillimetersPerSecond = value;
    }

    public HorizontalTimeScaleMode HorizontalTimeScaleMode
    {
        get => displaySettings.HorizontalTimeScaleMode;
        set => displaySettings.HorizontalTimeScaleMode = value;
    }

    public bool IsPaperSpeedMode
    {
        get => displaySettings.IsPaperSpeedMode;
        set
        {
            if (value)
            {
                HorizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed;
            }
        }
    }

    public bool IsTimebaseMode
    {
        get => displaySettings.IsTimebaseMode;
        set
        {
            if (value)
            {
                HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;
            }
        }
    }

    public double TimebaseSecondsPerScreen
    {
        get => displaySettings.TimebaseSecondsPerScreen;
        set => displaySettings.TimebaseSecondsPerScreen = value;
    }

    public double EffectiveTimebaseSeconds => VisibleDurationSeconds;

    public double DerivedPaperSpeedMillimetersPerSecond => displaySettings.ViewportWidthDips <= 0
        ? 0
        : displaySettings.DerivedPaperSpeedMillimetersPerSecond;

    public async Task InitializeAsync()
    {
        await session.InitializeAsync();
        await LoadEventsAsync();
        await LoadEnabledDefinitionsAsync();
        UpdateViewportStart(session.ViewportStartSeconds);
    }

    public async Task LoadEventsAsync()
    {
        if (eventStore is null) return;
        var events = await eventStore.LoadAsync(CancellationToken.None);
        RecordingEvents.Clear();
        foreach (var item in events.OrderBy(item => item.StartSample)) RecordingEvents.Add(item);
        RaisePropertyChanged(nameof(RecordingEvents));
        RaisePropertyChanged(nameof(CanEditSelectedEvent));
    }

    public async Task CreateManualEventAsync(string definitionId, string? note = null)
    {
        if (eventStore is null || EventDefinitionService is null)
            throw new InvalidOperationException("当前回溯记录不支持事件编辑。");
        var service = new RecordingEventService(recordingId, eventStore, EventDefinitionService);
        await service.CreateAsync(
            definitionId,
            (long)Math.Round(PositionSeconds * samplingRateHz, MidpointRounding.AwayFromZero),
            0,
            EventSource.ManualButton,
            "review-toolbar",
            null,
            note,
            CancellationToken.None);
        await LoadEventsAsync();
    }

    public async Task DeleteSelectedEventAsync()
    {
        if (eventStore is null || EventDefinitionService is null || SelectedRecordingEvent is not { } item)
            return;
        var service = new RecordingEventService(recordingId, eventStore, EventDefinitionService);
        await service.DeleteAsync(item.Id, CancellationToken.None);
        SelectedRecordingEvent = null;
        await LoadEventsAsync();
    }

    private async Task LoadEnabledDefinitionsAsync()
    {
        EnabledEventDefinitions.Clear();
        if (EventDefinitionService is null) return;
        foreach (var definition in (await EventDefinitionService.ListAsync(CancellationToken.None)).Where(item => item.IsEnabled))
            EnabledEventDefinitions.Add(definition);
        SelectedEventDefinitionId ??= EnabledEventDefinitions.FirstOrDefault()?.Id;
    }

    private async Task MarkSelectedEventAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedEventDefinitionId))
            throw new InvalidOperationException("请先选择一个事件定义。");

        await CreateManualEventAsync(SelectedEventDefinitionId);
        notifications?.PublishSuccess("事件已标记。");
    }

    private async Task DeleteSelectedEventWithNotificationAsync()
    {
        if (!CanEditSelectedEvent)
            throw new InvalidOperationException("只能删除人工创建的事件标记。");

        await DeleteSelectedEventAsync();
        notifications?.PublishSuccess("事件已删除。");
    }

    private async Task SaveEditedEventAsync()
    {
        if (eventStore is null || EventDefinitionService is null || SelectedRecordingEvent is not { } item || !CanEditSelectedEvent)
            throw new InvalidOperationException("当前事件不可编辑。");
        if (!double.IsFinite(EditEventStartSeconds) || EditEventStartSeconds < 0 || EditEventStartSeconds > DurationSeconds)
            throw new EventValidationException("event_start_invalid", "事件起始时间超出当前 Recording 范围。");
        if (!double.IsFinite(EditEventDurationSeconds) || EditEventDurationSeconds < 0 || EditEventStartSeconds + EditEventDurationSeconds > DurationSeconds + 1d / samplingRateHz)
            throw new EventValidationException("event_duration_invalid", "事件持续时间超出当前 Recording 范围。");

        var startSample = checked((long)Math.Round(EditEventStartSeconds * samplingRateHz, MidpointRounding.AwayFromZero));
        var durationSamples = checked((long)Math.Round(EditEventDurationSeconds * samplingRateHz, MidpointRounding.AwayFromZero));
        var service = new RecordingEventService(recordingId, eventStore, EventDefinitionService);
        await service.UpdateAsync(item with
        {
            StartSample = startSample,
            DurationSamples = durationSamples,
            Note = string.IsNullOrWhiteSpace(EditEventNote) ? null : EditEventNote.Trim(),
        }, CancellationToken.None);
        await LoadEventsAsync();
        SelectedRecordingEvent = RecordingEvents.FirstOrDefault(value => value.Id == item.Id);
        IsEventEditorOpen = false;
        notifications?.PublishSuccess("事件已更新。");
    }

    private void LoadSelectedEventEditor(RecordingEvent? item)
    {
        EditEventStartSeconds = item is null ? 0 : item.StartSample / (double)samplingRateHz;
        EditEventDurationSeconds = item is { IsInterval: true } ? item.DurationSamples / (double)samplingRateHz : 0;
        EditEventNote = item?.Note ?? string.Empty;
    }

    private void ReportCommandError(Exception exception) => notifications?.PublishError(exception.Message);

    public Task SeekToEventAsync(RecordingEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return SeekAsync(item.StartSample / (double)samplingRateHz);
    }

    public Task SeekAsync(double positionSeconds)
    {
        SetPreviewPosition(positionSeconds);
        return session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    public Task SeekRelativeAsync(double seconds) => SeekAsync(PositionSeconds + seconds);

    public async Task NavigateFromTrackClickAsync(double positionSeconds)
    {
        var targetPosition = Math.Clamp(positionSeconds, 0, DurationSeconds);
        var targetViewportStart = GetFollowViewportStart(targetPosition);
        if (!await session.NavigateFromTrackClickAsync(targetViewportStart, targetPosition))
        {
            return;
        }

        // The session has atomically published the target frame and viewport.
        // Move the playback cursor only after that publication, so a track
        // click never produces a slider position with an older waveform.
        playback.Seek(targetPosition);
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
    }

    public Task SeekPageAsync(int direction) => SeekRelativeAsync(direction * VisibleDurationSeconds);

    /// <summary>
    /// Dragging updates the accepted scrollbar position continuously. Loading is
    /// throttled rather than debounced, so it starts before mouse-up.
    /// </summary>
    public void PreviewSeek(double positionSeconds)
    {
        SetPreviewPosition(positionSeconds);
        ScheduleViewportLoad();
        PrefetchWhenApproachingCacheEdge();
    }

    public Task SetVisibleDurationAsync(double visibleDurationSeconds) =>
        session.SetVisibleDurationAsync(visibleDurationSeconds, playback.PositionSeconds);

    public void UpdateViewportWidth(double widthDips)
    {
        if (!double.IsFinite(widthDips) || widthDips <= 0 || Math.Abs(widthDips - displaySettings.ViewportWidthDips) < 0.5)
        {
            return;
        }

        displaySettings.ViewportWidthDips = widthDips;
    }

    public void UpdateScreenScale(ScreenScaleContext value)
    {
        displaySettings.ScreenScale = value;
    }

    public Task SelectViewingMontageAsync(MontageProfile? montage) =>
        session.SelectViewingMontageAsync(montage, playback.PositionSeconds);

    public void TogglePlayback()
    {
        if (playback.IsPlaying)
        {
            playback.Pause();
        }
        else
        {
            playback.Play();
        }

        RaisePlaybackPropertiesChanged();
    }

    public void CyclePlaybackSpeed()
    {
        var next = playback.PlaybackRate switch
        {
            0.5d => 1d,
            1d => 2d,
            _ => 0.5d,
        };
        playback.SetPlaybackRate(next);
        RaisePropertyChanged(nameof(PlaybackSpeedText));
    }

    public void TickPlayback()
    {
        if (!playback.Update())
        {
            return;
        }

        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
        var desiredViewportStart = GetFollowViewportStart(playback.PositionSeconds);
        UpdateViewportStart(desiredViewportStart);
        if (!session.ContainsViewport(desiredViewportStart) &&
            !session.TryActivateCachedViewport(desiredViewportStart, playback.PositionSeconds))
        {
            StartPlaybackLoad();
        }

        PrefetchWhenApproachingCacheEdge();
    }

    public async ValueTask DisposeAsync()
    {
        viewportLoader.Dispose();
        filterUpdater.Dispose();
        displaySettings.PropertyChanged -= OnDisplaySettingsChanged;
        session.Changed -= OnSessionChanged;
        await session.DisposeAsync();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(AcquisitionMontageText));
        RaisePropertyChanged(nameof(CurrentViewingMontage));
        if (!ReferenceEquals(publishedFrame, session.CurrentFrame))
        {
            publishedFrame = session.CurrentFrame;
            RaisePropertyChanged(nameof(CurrentFrame));
        }
        UpdateViewportStart(session.ViewportStartSeconds);
        RaisePropertyChanged(nameof(WindowTimeText));
        RaisePropertyChanged(nameof(VisibleDurationSeconds));
        RaisePropertyChanged(nameof(OutputChannelCount));
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(PreparationState));
        RaisePropertyChanged(nameof(PreparationProgress));
        RaisePlaybackPropertiesChanged();
    }

    private void RaisePlaybackPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPlaying));
        RaisePropertyChanged(nameof(IsCompleted));
    }

    private void ScheduleFilterUpdate()
    {
        filterUpdater.Schedule(TimeSpan.FromMilliseconds(220), async cancellationToken =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await session.SetFilterAsync(new RecordingReviewFilterSettings(
                    HighPassHz, LowPassHz, NotchHz <= 0 ? null : NotchHz), playback.PositionSeconds);
            }
            catch (ArgumentException)
            {
                // A transient invalid pair can occur while two selectors update.
            }
        });
    }

    private void ScheduleViewportLoad()
    {
        viewportLoader.Schedule(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.LoadViewportAsync(ViewportStartSeconds, playback.PositionSeconds);
        });
    }

    private void StartPlaybackLoad()
    {
        // Do not debounce repeatedly at render cadence. One foreground request
        // keeps running while playback advances; it fills a reusable cache block.
        if (playbackLoadTask is { IsCompleted: false })
        {
            return;
        }

        playbackLoadTask = LoadPlaybackBlockAsync(ViewportStartSeconds, playback.PositionSeconds);
    }

    private async Task LoadPlaybackBlockAsync(double viewportStart, double position)
    {
        await session.LoadViewportAsync(viewportStart, position);
    }

    private void PrefetchWhenApproachingCacheEdge()
    {
        var leadSeconds = Math.Max(5, VisibleDurationSeconds * 0.75);
        if (session.IsLoading || !session.ContainsViewport(ViewportStartSeconds) || session.CurrentFrame is not { } frame ||
            frame.WindowEndSeconds - (ViewportStartSeconds + VisibleDurationSeconds) > leadSeconds ||
            frame.WindowEndSeconds >= DurationSeconds ||
            prefetchTask is { IsCompleted: false })
        {
            return;
        }

        prefetchTask = session.PrefetchNextAsync();
    }

    private void SetPreviewPosition(double positionSeconds)
    {
        playback.Seek(positionSeconds);
        UpdateViewportStart(GetFollowViewportStart(playback.PositionSeconds));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(PositionTimeText));
        RaisePlaybackPropertiesChanged();
    }
    private double GetFollowViewportStart(double positionSeconds)
    {
        var maximum = Math.Max(0, DurationSeconds - VisibleDurationSeconds);
        return Math.Clamp(positionSeconds - VisibleDurationSeconds * 0.25, 0, maximum);
    }
    private void UpdateViewportStart(double value)
    {
        var bounded = Math.Clamp(value, 0, Math.Max(0, DurationSeconds - VisibleDurationSeconds));
        if (Math.Abs(viewportStartSeconds - bounded) < 0.000_001)
            return;

        viewportStartSeconds = bounded;
        RaisePropertyChanged(nameof(ViewportStartSeconds));
        RaisePropertyChanged(nameof(WindowTimeText));
    }
    private void ScheduleViewportDurationUpdate()
    {
        // A timebase is an explicit seconds-per-screen value and must work
        // before the SciChart host reports its final width. Paper-speed mode
        // needs the calibrated physical width and waits for that measurement.
        if ((HorizontalTimeScaleMode == HorizontalTimeScaleMode.PaperSpeed && displaySettings.ViewportWidthDips <= 0) ||
            viewportDurationUpdateTask is { IsCompleted: false })
        {
            if (viewportDurationUpdateTask is { IsCompleted: false })
            {
                viewportDurationUpdatePending = true;
            }

            return;
        }

        var requestedDuration = displaySettings.GetVisibleSeconds(displaySettings.ViewportWidthDips);
        var visibleDuration = Math.Clamp(requestedDuration, 1d, DurationSeconds);
        if (Math.Abs(visibleDuration - VisibleDurationSeconds) < 0.02)
        {
            return;
        }

        viewportDurationUpdateTask = UpdateVisibleDurationAsync(visibleDuration);
    }
    private async Task UpdateVisibleDurationAsync(double visibleDuration)
    {
        try
        {
            await session.SetVisibleDurationAsync(visibleDuration, playback.PositionSeconds);
        }
        finally
        {
            viewportDurationUpdateTask = null;
            var shouldRerun = viewportDurationUpdatePending;
            viewportDurationUpdatePending = false;
            // ScheduleViewportDurationUpdate applies the mode-specific guard:
            // timebase mode is valid before the chart reports a width, while
            // paper-speed mode must wait for calibrated viewport geometry.
            if (shouldRerun)
            {
                ScheduleViewportDurationUpdate();
            }
        }
    }

    private string FormatElapsedTime(double seconds) =>
        $"{Math.Clamp(seconds, 0, DurationSeconds):0.0} s";

    private void OnDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(e.PropertyName);
        RaiseHorizontalScalePropertiesChanged();
        if (e.PropertyName is nameof(WaveformDisplaySettings.PaperSpeedMillimetersPerSecond)
            or nameof(WaveformDisplaySettings.HorizontalTimeScaleMode)
            or nameof(WaveformDisplaySettings.TimebaseSecondsPerScreen)
            or nameof(WaveformDisplaySettings.ViewportWidthDips)
            or nameof(WaveformDisplaySettings.ScreenScale))
        {
            ScheduleViewportDurationUpdate();
        }
    }

    private void RaiseHorizontalScalePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPaperSpeedMode));
        RaisePropertyChanged(nameof(IsTimebaseMode));
        RaisePropertyChanged(nameof(EffectiveTimebaseSeconds));
        RaisePropertyChanged(nameof(DerivedPaperSpeedMillimetersPerSecond));
    }
}

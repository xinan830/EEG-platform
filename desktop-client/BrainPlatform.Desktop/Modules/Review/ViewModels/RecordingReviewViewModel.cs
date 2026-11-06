using System.Collections.ObjectModel;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Review.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Review.Waveform;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed partial class RecordingReviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RecordingReviewSession session;
    private readonly WorkspaceMontageSelection<MontageProfile> montageSelection;
    private readonly IRecordingAccessSession reviewReader;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly RecordingPlaybackTimeCoordinator playback;
    private readonly bool ownsPlayback;
    private readonly int samplingRateHz;
    private readonly int sourceChannelCount;
    private readonly string projectName;
    private readonly string recordingName;
    private readonly DateTimeOffset recordingStartUtc;
    private readonly RecordingEventStore? eventStore;
    private readonly string recordingId;
    private readonly OperationNotificationCenter? notifications;
    private readonly IReadOnlyDictionary<string, ReviewAnalysisCoordinator> analysisCoordinators;
    private readonly IReadOnlyDictionary<string, ReviewAlgorithmPanelRegistration> analysisPanels;
    private ReviewAnalysisCoordinator? analysisCoordinator;
    private ReviewAnalysisBinding? analysisBinding;
    private readonly LatestDelayedTask filterUpdater = new();
    private double highPassHz = 1;
    private double lowPassHz = 30;
    private double notchHz = 50;
    private readonly WaveformDisplaySettings displaySettings = new();
    private Task? viewportDurationUpdateTask;
    private bool viewportDurationUpdatePending;
    private double viewportStartSeconds;
    private RecordingReviewFrame? publishedFrame;
    private double publishedVisibleDuration;
    private string analysisPreparationText = "算法未准备。";
    private string? activeAnalysisId;
    private double analysisPanelStartSeconds;
    private readonly string? analysisRecordingDirectory;
    private RegisteredRecording? analysisRecording;
    private readonly CancellationTokenSource analysisLifetime = new();
    public ReviewRbpParameters RbpParameters { get; } = new();
    public RbpReviewAnalysisViewModel RbpAnalysis { get; } = new();
    public ReviewStftParameters StftParameters { get; } = new();
    public StftReviewAnalysisViewModel StftAnalysis { get; } = new();
    public ReviewTnkParameters TnkParameters { get; } = new();
    public ReviewTnkAnalysisViewModel TnkAnalysis { get; } = new();
    public ReviewScalarParameters FaaParameters { get; } = new("faa", "F3", "F4", "f4_channel",
        new Dictionary<string, object?> { ["low_hz"] = 1d, ["high_hz"] = 50d, ["notch_hz"] = 50d });
    public ReviewScalarParameters IapfParameters { get; } = new("iapf");
    public ReviewScalarParameters ThetaBetaParameters { get; } = new("theta_beta");
    public ReviewScalarParameters BrainbeatParameters { get; } = new("brainbeat", "Fz", "Pz", "secondary_channel");
    public ReviewScalarParameters IndividualizedBrainbeatParameters { get; } = new("brainbeat_individualized", "Fz", "Pz", "secondary_channel");
    public ReviewScalarAnalysisViewModel FaaAnalysis { get; } = new("无量纲", ReviewScalarProjectors.Faa);
    public ReviewScalarAnalysisViewModel IapfAnalysis { get; } = new("Hz", ReviewScalarProjectors.Iapf);
    public ReviewScalarAnalysisViewModel ThetaBetaAnalysis { get; } = new("无量纲", ReviewScalarProjectors.ThetaBeta);
    public ReviewScalarAnalysisViewModel BrainbeatAnalysis { get; } = new("ratio", ReviewScalarProjectors.Brainbeat);
    private long analysisConfigurationRevision;
    private bool isAnalysisStale;

    public RecordingReviewViewModel(
        IRecordingAccessSession reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds = 10,
        string? recordingName = null,
        string? projectName = null,
        IRecordingReviewFilter? filter = null,
        Func<double>? playbackClockSeconds = null,
        string? recordingDirectory = null,
        EventDefinitionService? eventDefinitionService = null,
        OperationNotificationCenter? notifications = null,
        IReadOnlyList<ReviewAnalysisCoordinator>? analysisCoordinators = null,
        RecordingTimeDisplaySettings? timeDisplaySettings = null,
        RecordingPlaybackTimeCoordinator? playbackCoordinator = null,
        IAlgorithmClient? algorithmClient = null,
        BrainPlatform.Desktop.Modules.Review.Export.IRecordingSignalExportClient? signalExportClient = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        reviewReader = reader;
        this.catalog = catalog;
        recordingId = reader.Manifest.SessionId.ToString("N");
        montageSelection = new WorkspaceMontageSelection<MontageProfile>(recordingId, catalog.AcquisitionMontage,
            catalog.CompatibleViewingMontages.Select(item => item.Profile));
        session = new RecordingReviewSession(reader, catalog, montageSelection, visibleDurationSeconds, filter);
        samplingRateHz = reader.Manifest.SamplingRateHz;
        var durationSamples = checked((long)Math.Round(
            reader.DurationSeconds * samplingRateHz, MidpointRounding.AwayFromZero));
        playback = playbackCoordinator ?? new RecordingPlaybackTimeCoordinator(
            recordingId, samplingRateHz, durationSamples, playbackClockSeconds);
        ownsPlayback = playbackCoordinator is null;
        if (!string.Equals(playback.RecordingId, recordingId, StringComparison.Ordinal) ||
            playback.SamplingRateHz != samplingRateHz || playback.DurationSamples != durationSamples)
        {
            throw new ArgumentException("播放协调器与当前录音身份或采样范围不一致。", nameof(playbackCoordinator));
        }
        recordingStartUtc = reader.Manifest.RecordingStartUtc;
        InitializeTimeDisplay(timeDisplaySettings ?? new RecordingTimeDisplaySettings());
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
        analysisPanels = ReviewAlgorithmPanels.Create(this, montageSelection);
        this.analysisCoordinators = analysisCoordinators is not null
            ? analysisCoordinators.ToDictionary(item => item.AlgorithmId, StringComparer.Ordinal)
            : algorithmClient is null ? new Dictionary<string, ReviewAnalysisCoordinator>()
            : analysisPanels.Values.ToDictionary(item => item.AlgorithmId,
                item => item.CreateCoordinator(algorithmClient), StringComparer.Ordinal);
        analysisCoordinator = this.analysisCoordinators.Values.FirstOrDefault();
        analysisRecordingDirectory = recordingDirectory;
        this.signalExportClient = signalExportClient ?? algorithmClient as BrainPlatform.Desktop.Modules.Review.Export.IRecordingSignalExportClient;
        MarkSelectedEventCommand = new AsyncRelayCommand(MarkSelectedEventAsync, ReportCommandError);
        DeleteSelectedEventCommand = new AsyncRelayCommand(DeleteSelectedEventWithNotificationAsync, ReportCommandError);
        SaveEditedEventCommand = new AsyncRelayCommand(SaveEditedEventAsync, ReportCommandError);
        OpenRbpAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("rbp"), ReportCommandError);
        OpenStftAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("stft"), ReportCommandError);
        InitializePsdAnalysis();
        InitializeSpectralValues();
        InitializeCustomFrequencyInspection();
        OpenTnkAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("tnk_biofeedback"), ReportCommandError);
        OpenFaaAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("faa"), ReportCommandError);
        OpenIapfAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("iapf"), ReportCommandError);
        OpenThetaBetaAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("theta_beta"), ReportCommandError);
        OpenBrainbeatAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("brainbeat"), ReportCommandError);
        OpenIndividualizedBrainbeatAnalysisCommand = new AsyncRelayCommand(() => OpenAnalysisAsync("brainbeat_individualized"), ReportCommandError);
        CloseAnalysisCommand = new AsyncRelayCommand(CloseAnalysisAsync, ReportCommandError);
        foreach (var parameters in analysisPanels.Values.Select(item => item.Configuration).Distinct())
            parameters.PropertyChanged += OnAnalysisConfigurationChanged;
        RbpAnalysis.PropertyChanged += OnRbpTopomapPropertyChanged;
        OpenEventEditorCommand = new RelayCommand(() => IsEventEditorOpen = true, () => CanEditSelectedEvent);
        CloseEventEditorCommand = new RelayCommand(() => IsEventEditorOpen = false);
        displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        session.Changed += OnSessionChanged;
    }

    public IReadOnlyList<MontageProfile> CompatibleViewingMontages => montageSelection.AvailableMontages;

    internal WorkspaceMontageSelection<MontageProfile> WorkspaceMontageSelection => montageSelection;

    public bool HasCompatibleViewingMontages => CompatibleViewingMontages.Count > 0;

    public string MontageSelectionHint => HasCompatibleViewingMontages
        ? "默认使用采集时导联快照；仅可切换同一通道配置且映射兼容的导联，不修改原始记录。"
        : catalog.Incompatible.FirstOrDefault()?.Reason ?? "当前记录没有可用的兼容导联，显示原始通道。";

    public ObservableCollection<RecordingEvent> RecordingEvents { get; } = [];
    public ObservableCollection<RecordingEventOption> RecordingEventOptions { get; } = [];
    public ObservableCollection<EventDefinition> EnabledEventDefinitions { get; } = [];

    public IReadOnlyList<RecordingLifecycleBoundary> LifecycleBoundaries => reviewReader.LifecycleBoundaries;

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
    public double EditEventStartSeconds
    {
        get => editEventStartSeconds;
        set
        {
            if (SetProperty(ref editEventStartSeconds, value)) RaisePropertyChanged(nameof(EditEventClockText));
        }
    }
    public string EditEventClockText => double.IsFinite(EditEventStartSeconds) &&
        EditEventStartSeconds >= 0 && EditEventStartSeconds <= DurationSeconds
        ? RecordingEventDisplayTime.FormatClock(
            recordingStartUtc,
            (long)Math.Round(EditEventStartSeconds * samplingRateHz, MidpointRounding.AwayFromZero),
            samplingRateHz)
        : "时间无效";
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

    public System.Windows.Input.ICommand OpenRbpAnalysisCommand { get; }

    public System.Windows.Input.ICommand OpenStftAnalysisCommand { get; }
    public System.Windows.Input.ICommand OpenTnkAnalysisCommand { get; }

    public System.Windows.Input.ICommand OpenFaaAnalysisCommand { get; }
    public System.Windows.Input.ICommand OpenIapfAnalysisCommand { get; }
    public System.Windows.Input.ICommand OpenThetaBetaAnalysisCommand { get; }
    public System.Windows.Input.ICommand OpenBrainbeatAnalysisCommand { get; }
    public System.Windows.Input.ICommand OpenIndividualizedBrainbeatAnalysisCommand { get; }

    public System.Windows.Input.ICommand CloseAnalysisCommand { get; }

    public bool IsAnalysisPanelOpen => activeAnalysisId is not null;
    public ReviewAlgorithmPanelRegistration? ActiveAnalysisPanel => activeAnalysisId is { } id &&
        analysisPanels.TryGetValue(id, out var panel) ? panel : null;
    public ReviewAnalysisPanelKind? ActiveAnalysisPanelKind => IsCustomFrequencyInspectionPanelOpen
        ? ReviewAnalysisPanelKind.CustomFrequencyInspection
        : IsSpectralValuesPanelOpen ? ReviewAnalysisPanelKind.SpectralValues : ActiveAnalysisPanel?.PanelKind;
    public bool IsRbpAnalysisPanelOpen => ActiveAnalysisPanelKind == ReviewAnalysisPanelKind.Rbp;
    public bool IsPsdAnalysisPanelOpen => ActiveAnalysisPanelKind == ReviewAnalysisPanelKind.Psd;
    public bool IsStftAnalysisPanelOpen => ActiveAnalysisPanel?.PanelKind == ReviewAnalysisPanelKind.Stft;
    public bool IsTnkAnalysisPanelOpen => ActiveAnalysisPanel?.PanelKind == ReviewAnalysisPanelKind.Tnk;
    public bool IsFaaAnalysisPanelOpen => ActiveAnalysisPanel?.AlgorithmId == "faa";
    public bool IsIapfAnalysisPanelOpen => ActiveAnalysisPanel?.AlgorithmId == "iapf";
    public bool IsThetaBetaAnalysisPanelOpen => ActiveAnalysisPanel?.AlgorithmId == "theta_beta";
    public bool IsBrainbeatAnalysisPanelOpen => ActiveAnalysisPanel?.AlgorithmId is "brainbeat" or "brainbeat_individualized";
    public ReviewScalarParameters? ActiveScalarParameters => ActiveAnalysisPanel?.Configuration as ReviewScalarParameters;
    public ReviewScalarAnalysisViewModel? ActiveScalarAnalysis => ActiveAnalysisPanel?.PanelKind == ReviewAnalysisPanelKind.Scalar
        ? ActiveAnalysisPanel.Presentation as ReviewScalarAnalysisViewModel : null;
    public bool HasSecondaryScalarChannel => activeAnalysisId is "faa" or "brainbeat" or "brainbeat_individualized";
    public string AnalysisPanelTitle => IsCustomFrequencyInspectionPanelOpen ? "自定义频段" :
        IsSpectralValuesPanelOpen ? "频段数值核查" : ActiveAnalysisPanel?.DisplayName ?? "算法分析";

    public string AnalysisPreparationText
    {
        get => analysisPreparationText;
        private set => SetProperty(ref analysisPreparationText, value);
    }

    public bool CanEditSelectedEvent => SelectedRecordingEvent is { Source: EventSource.ManualButton or EventSource.KeyboardShortcut };

    public WaveformDisplaySettings DisplaySettings => displaySettings;

    public string AcquisitionMontageText => catalog.AcquisitionMontageStatusText;

    public MontageProfile? AcquisitionMontage => session.AcquisitionMontage;

    public MontageProfile? CurrentViewingMontage => montageSelection.SelectedMontage;

    public RecordingReviewFrame? CurrentFrame => session.CurrentFrame;

    internal Task<ReviewDisplayFrame?> TryBuildDisplayFrameAsync(
        RecordingReviewFrame frame,
        int plotWidthPixels,
        CancellationToken cancellationToken) =>
        session.TryBuildDisplayFrameAsync(frame, ViewportStartSeconds,
            VisibleDurationSeconds, plotWidthPixels, cancellationToken);

    public double PositionSeconds => playback.PositionSeconds;

    public double AnalysisPanelStartSeconds => analysisPanelStartSeconds;

    public Guid ReviewSessionId => session.ReviewSessionId;

    /// <summary>The left edge shown by the chart. This changes at render cadence while navigating.</summary>
    public double ViewportStartSeconds => viewportStartSeconds;

    public double DurationSeconds => session.DurationSeconds;

    public int SamplingRateHz => samplingRateHz;
    public string RecordingId => recordingId;

    public int SourceChannelCount => sourceChannelCount;

    public string ProjectName => projectName;

    public string RecordingName => recordingName;

    public DateTimeOffset RecordingStartUtc => recordingStartUtc;
    public long RecordingFirstSampleCounter => reviewReader.FirstSampleCounter;

    public double VisibleDurationSeconds => session.VisibleDurationSeconds;

    public int OutputChannelCount => session.CurrentFrame?.OutputChannelNames.Count ?? 0;

    public bool IsLoading => session.IsLoading;

    public bool IsPlaying => playback.IsPlaying;

    public bool IsCompleted => playback.IsCompleted;

    public string PlaybackSpeedText => $"{playback.PlaybackRate:0.0#}X";

    public string StatusText => session.StatusText;

    public WindowPreviewResponse? CurrentAnalysisWindow => isAnalysisStale ? null : analysisBinding?.CurrentWindow;

    /// <summary>Requested cursor and displayed backend window are intentionally separate.</summary>
    public long TargetAnalysisCursorSample => analysisBinding?.TargetCursorSample ?? -1;

    public long? DisplayedAnalysisCursorSample => isAnalysisStale ? null : analysisBinding?.DisplayedCursorSample;

    public string AnalysisWindowState => isAnalysisStale ? "stale" : analysisBinding?.DisplayedWindowStatus ?? "none";

    public bool IsAnalysisTargetDisplayed => !isAnalysisStale && analysisBinding?.IsTargetDisplayed == true;

    public bool IsAnalysisLoading => analysisBinding?.IsLoading ?? false;

    public string AnalysisStatusText => isAnalysisStale ? "参数已改变，等待自动分析。" : analysisBinding?.StatusText ?? "未绑定分析结果。";

    public bool IsAnalysisAttached => analysisBinding?.RunId is not null || analysisBinding?.IsOnDemandAttached == true;

    public ReviewPreparationState PreparationState => session.PreparationState;

    public double? PreparationProgress => PreparationState.Progress;

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
        var selectedId = SelectedRecordingEvent?.Id;
        var events = await eventStore.LoadAsync(CancellationToken.None);
        SelectedRecordingEvent = null;
        RecordingEvents.Clear();
        RecordingEventOptions.Clear();
        foreach (var item in events.OrderBy(item => item.StartSample))
        {
            RecordingEvents.Add(item);
            RecordingEventOptions.Add(new RecordingEventOption(
                item,
                $"{TimeDisplay.FormatPoint(item.StartSample / (double)samplingRateHz)} {item.DefinitionSnapshot.Name}",
                RecordingEventDisplayTime.FormatMarker(item, recordingStartUtc, samplingRateHz)));
        }
        SelectedRecordingEvent = RecordingEvents.FirstOrDefault(item => item.Id == selectedId);
        RaisePropertyChanged(nameof(RecordingEvents));
        RaisePropertyChanged(nameof(CanEditSelectedEvent));
    }

    public async Task CreateManualEventAsync(string definitionId, string? note = null)
        => await CreateEventAsync(definitionId, EventSource.ManualButton, "review-toolbar", note);

    public async Task CreateKeyboardEventAsync(string definitionId, string shortcut)
        => await CreateEventAsync(definitionId, EventSource.KeyboardShortcut, shortcut);

    public void ReportEventShortcutError(Exception exception) => ReportCommandError(exception);

    private async Task CreateEventAsync(string definitionId, EventSource source, string sourceDetail, string? note = null)
    {
        if (eventStore is null || EventDefinitionService is null)
            throw new InvalidOperationException("当前回溯记录不支持事件编辑。");
        var service = new RecordingEventService(recordingId, eventStore, EventDefinitionService);
        await service.CreateAsync(
            definitionId,
            (long)Math.Round(PositionSeconds * samplingRateHz, MidpointRounding.AwayFromZero),
            0,
            source,
            sourceDetail,
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
        EditEventStartSeconds = item is null ? 0 : RecordingTimeMapper.ToElapsedSeconds(item.StartSample, samplingRateHz);
        EditEventDurationSeconds = item is { IsInterval: true } ? item.DurationSamples / (double)samplingRateHz : 0;
        EditEventNote = item?.Note ?? string.Empty;
    }

    private void ReportCommandError(Exception exception) => notifications?.PublishError(exception.Message);

    public Task SeekToEventAsync(RecordingEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsDisplayable)
            throw new EventValidationException("event_coordinate_unavailable", item.CoordinateUnavailableReason ?? "该事件没有可用的波形坐标。");
        return SeekAsync(RecordingTimeMapper.ToElapsedSeconds(item.StartSample, samplingRateHz));
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

    public async Task SelectViewingMontageAsync(MontageProfile? montage)
    {
        var previous = montageSelection.Capture();
        if (!montageSelection.Select(montage)) return;
        var selected = montageSelection.Capture();

        RaisePropertyChanged(nameof(CurrentViewingMontage));
        if (analysisRecording is { } registered)
            UpdateAlgorithmMontageInputs(registered, previous, selected);
        InvalidateAnalysisConfiguration();
        await session.RefreshMontageSelectionAsync(playback.PositionSeconds);
    }

    public void ReportMontageSelectionError(Exception exception) => ReportCommandError(exception);

    public async ValueTask DisposeAsync()
    {
        if (isDisposing) return;
        isDisposing = true;
        playback.Pause();
        analysisLifetime.Cancel();
        analysisPreparation?.Cancel();
        analysisUpdater.Dispose();
        CustomFrequencyInspection.PropertyChanged -= OnCustomFrequencyInspectionChanged;
        foreach (var parameters in analysisPanels.Values.Select(item => item.Configuration).Distinct())
            parameters.PropertyChanged -= OnAnalysisConfigurationChanged;
        TimeDisplay.PropertyChanged -= OnTimeDisplayChanged;
        viewportLoader.Dispose();
        filterUpdater.Dispose();
        displaySettings.PropertyChanged -= OnDisplaySettingsChanged;
        session.Changed -= OnSessionChanged;
        await analysisPreparationGate.WaitAsync();
        try { await DetachAnalysisAsync(); }
        finally { analysisPreparationGate.Release(); }
        await session.DisposeAsync();
        if (ownsPlayback) playback.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(AcquisitionMontageText));
        RaisePropertyChanged(nameof(CurrentViewingMontage));
        if (!ReferenceEquals(publishedFrame, session.CurrentFrame))
        {
            // A drag preview keeps the previous chart viewport until the
            // replacement frame is complete. Commit the viewport immediately
            // before publishing that frame so SciChart never renders a new
            // axis against an old/partial frame.
            if (pendingPreviewViewportStart is { } pending &&
                session.CurrentFrame is not null &&
                Math.Abs(session.ViewportStartSeconds - pending) < 0.000_001)
            {
                pendingPreviewViewportStart = null;
                UpdateViewportStart(pending);
            }
            publishedFrame = session.CurrentFrame;
            RaisePropertyChanged(nameof(CurrentFrame));
        }
        RaisePropertyChanged(nameof(WindowTimeText));
        if (Math.Abs(publishedVisibleDuration - session.VisibleDurationSeconds) > 0.000_001)
        {
            publishedVisibleDuration = session.VisibleDurationSeconds;
            UpdateViewportStart(GetFollowViewportStart(playback.PositionSeconds));
            RaisePropertyChanged(nameof(VisibleDurationSeconds));
        }
        RaisePropertyChanged(nameof(OutputChannelCount));
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(PreparationState));
        RaisePropertyChanged(nameof(PreparationProgress));
        TnkAnalysis.SetCursor(PositionSeconds);
    }


    private void PublishPlaybackChanges(bool wasPlaying, bool wasCompleted)
    {
        if (wasPlaying != IsPlaying) RaisePropertyChanged(nameof(IsPlaying));
        if (wasCompleted != IsCompleted) RaisePropertyChanged(nameof(IsCompleted));
        TnkAnalysis.SetCursor(PositionSeconds);
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

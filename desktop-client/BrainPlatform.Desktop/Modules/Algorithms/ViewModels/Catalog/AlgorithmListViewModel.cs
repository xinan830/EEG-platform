using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;
using BrainPlatform.Desktop.Modules.Montages.Stores;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAlgorithmClient client;
    private readonly AlgorithmRunCoordinator runCoordinator;
    private readonly OperationNotificationCenter notifications;
    private readonly MontageProfileStore montageStore;
    private string statusText = "尚未加载算法目录。";
    private bool isLoading;
    private bool isLoadFailed;
    private string sourceDirectory = string.Empty;
    private RegisteredRecording? registeredRecording;
    private AlgorithmCatalogItem? selectedAlgorithm;
    private string? selectedAlgorithmId;
    private ProjectRecordingRow? selectedProjectRecording;
    private string runStatusText = "尚未提交分析。";
    private string resultSummaryText = "暂无结果摘要。";
    private string provenanceText = "暂无溯源信息。";
    private string structuredPreviewText = "暂无结构化预览。";
    private string dynamicSeriesText = "暂无动态结果。";
    private string selectedAnalysisMode = "静态";
    private double selectedDynamicWindowSeconds = 5.0;
    private string dynamicStepText = "1";
    private string selectedDynamicExecutionMode = "全量分析";
    private string selectedChannel = string.Empty;
    private HistoricalMontageDescriptor? selectedMontageProfile;
    private string selectedF4Channel = string.Empty;
    private string startSecondsText = "0";
    private string endSecondsText = string.Empty;
    private string lowFrequencyText = "1";
    private string highFrequencyText = "50";
    private string selectedNotchFrequency = "50 Hz";
    private string numeratorLowFrequencyText = "4";
    private string numeratorHighFrequencyText = "8";
    private string denominatorLowFrequencyText = "8";
    private string denominatorHighFrequencyText = "13";
    private string selectedPeakBandPreset = "自定义";
    private string selectedRatioPreset = "自定义";
    private bool applyingBandPreset;
    private string searchText = string.Empty;
    private string selectedModeFilter = "全部";
    private string selectedAvailabilityFilter = "全部";
    private string selectedTypeFilter = "全部";
    private AnalysisRunResponse? lastRun;
    private readonly DispatcherTimer dynamicPreviewTimer;
    private readonly DispatcherTimer automaticRunDebounceTimer;
    private double dynamicPreviewCursorSeconds;
    private bool isDynamicPreviewPlaying;
    private bool automaticRunPending;

    public AlgorithmListViewModel(IAlgorithmClient client, OperationNotificationCenter notifications, ProjectWorkspaceViewModel? projects = null,
        RecordingTimeDisplaySettings? timeDisplaySettings = null, MontageProfileStore? montageStore = null)
    {
        InitializeTimeDisplay(timeDisplaySettings ?? new RecordingTimeDisplaySettings());
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        runCoordinator = new AlgorithmRunCoordinator(client);
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        this.montageStore = montageStore ?? new MontageProfileStore();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCatalogError);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, ReportRunError);
        RunCommand = new AsyncRelayCommand(RunAsync, ReportRunError);
        CancelRunCommand = new AsyncRelayCommand(CancelRunAsync, ReportRunError);
        CancelBackgroundFullRunCommand = new AsyncRelayCommand(CancelBackgroundFullRunAsync, ReportRunError);
        SelectStaticModeCommand = new RelayCommand(() => SelectedAnalysisMode = "静态");
        SelectDynamicModeCommand = new RelayCommand(() => SelectedAnalysisMode = "动态");
        StartDynamicPreviewCommand = new RelayCommand(StartDynamicPreview, CanStartDynamicPreview);
        PauseDynamicPreviewCommand = new RelayCommand(PauseDynamicPreview);
        StepDynamicPreviewCommand = new RelayCommand(StepDynamicPreview, CanStepDynamicPreview);
        ResetDynamicPreviewCommand = new RelayCommand(ResetDynamicPreview, CanResetDynamicPreview);
        dynamicPreviewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        dynamicPreviewTimer.Tick += (_, _) => AdvanceDynamicPreview();
        dynamicWindowRequestTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        dynamicWindowRequestTimer.Tick += (_, _) => FlushWindowRequest();
        automaticRunDebounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        automaticRunDebounceTimer.Tick += async (_, _) =>
        {
            automaticRunDebounceTimer.Stop();
            if (disposed || detailSuspended || !automaticRunPending || IsRunActive ||
                RegisteredRecording is null || SelectedAlgorithm is null)
                return;
            automaticRunPending = false;
            try { await RunAsync(); }
            catch (Exception exception) { ReportRunError(exception); }
        };
        psdPreview.PropertyChanged += (_, args) => RelayPreviewPropertyChanged(args.PropertyName, "Psd");
        stftPreview.PropertyChanged += (_, args) => RelayPreviewPropertyChanged(args.PropertyName, "Stft");
        Projects = projects;
    }

    private void RelayPreviewPropertyChanged(string? propertyName, string kind)
    {
        if (kind == "Psd")
        {
            RaisePropertyChanged(nameof(PsdFrequencyUnit));
            RaisePropertyChanged(nameof(PsdValueUnit));
            RaisePropertyChanged(nameof(HasPsdPreview));
            RaisePropertyChanged(nameof(PsdFrequencyRangeText));
            RaisePropertyChanged(nameof(PsdValueRangeText));
            RaisePropertyChanged(nameof(PsdStructuredPreview));
            RaisePropertyChanged(nameof(PsdPreviewPoints));
            RaisePropertyChanged(nameof(PsdBandSharePoints));
            RaisePropertyChanged(nameof(PsdQualityText));
            RaisePropertyChanged(nameof(PsdWindowStateText));
        }
        else
        {
            RaisePropertyChanged(nameof(StftResult));
            RaisePropertyChanged(nameof(StftStructuredPreview));
            RaisePropertyChanged(nameof(HasStftPreview));
            RaisePropertyChanged(nameof(StftAxisText));
        }
    }

    public ObservableCollection<AlgorithmCatalogItem> Items { get; } = [];
    public ObservableCollection<AlgorithmCatalogItem> VisibleItems { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelRunCommand { get; }
    public ICommand CancelBackgroundFullRunCommand { get; }
    public ICommand SelectStaticModeCommand { get; }
    public ICommand SelectDynamicModeCommand { get; }
    public ICommand StartDynamicPreviewCommand { get; }
    public ICommand PauseDynamicPreviewCommand { get; }
    public ICommand StepDynamicPreviewCommand { get; }
    public ICommand ResetDynamicPreviewCommand { get; }
    public ProjectWorkspaceViewModel? Projects { get; }
    public ObservableCollection<string> RegisteredChannels { get; } = [];
    public ObservableCollection<TnkRoleChannelSelection> TnkRoleChannels { get; } = [];
    public ObservableCollection<HistoricalMontageDescriptor> MontageProfiles { get; } = [];
    public HistoricalMontageDescriptor? SelectedMontageProfile
    {
        get => selectedMontageProfile;
        set
        {
            var previousRole = selectedMontageProfile?.Outputs?
                .FirstOrDefault(output => string.Equals(output.Id, SelectedChannel, StringComparison.OrdinalIgnoreCase))?.Role
                ?? SelectedChannel;
            var previousSecondaryRole = selectedMontageProfile?.Outputs?
                .FirstOrDefault(output => string.Equals(output.Id, SelectedF4Channel, StringComparison.OrdinalIgnoreCase))?.Role
                ?? SelectedF4Channel;
            if (!SetProperty(ref selectedMontageProfile, value)) return;
            RegisteredChannels.Clear();
            foreach (var output in value?.Outputs?.Where(output => output.Available) ?? [])
                RegisteredChannels.Add(output.Id);
            var mapped = value?.Outputs?.FirstOrDefault(output =>
                string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
            SelectedChannel = mapped ?? RegisteredChannels.FirstOrDefault() ?? string.Empty;
            var mappedSecondary = value?.Outputs?.FirstOrDefault(output =>
                string.Equals(output.Role, previousSecondaryRole, StringComparison.OrdinalIgnoreCase))?.Id;
            if (!string.IsNullOrWhiteSpace(previousSecondaryRole))
                SelectedF4Channel = mappedSecondary ?? string.Empty;
            RefreshTnkRoleChannels();
            RaisePropertyChanged(nameof(HasRegisteredChannels));
            RaisePropertyChanged(nameof(AnalysisMontageText));
            ScheduleAutomaticRun();
        }
    }
    public string AnalysisMontageText => SelectedMontageProfile?.Label ?? "未选择历史导联配置";
    public string AnalysisMontageDetailsText => SelectedMontageProfile is not { } profile
        ? "分析导联：未选择历史配置"
        : $"分析导联：{string.Join("、", profile.Outputs.Where(output => output.Available).Select(output => $"{output.Role}→{output.Label}"))}；版本 {profile.Revision}；指纹 {profile.Fingerprint}";
    public IReadOnlyList<FrequencyBandPreset> FrequencyBandPresets { get; } =
    [
        new("自定义", null, null),
        new("Delta（1–4 Hz）", 1, 4),
        new("Theta（4–8 Hz）", 4, 8),
        new("Alpha（8–13 Hz）", 8, 13),
        new("Beta（13–30 Hz）", 13, 30),
    ];
    public IReadOnlyList<BandRatioPreset> BandRatioPresets { get; } =
    [
        new("自定义", null, null, null, null),
        new("Theta / Beta", 4, 8, 13, 30),
        new("Alpha / Theta", 8, 13, 4, 8),
        new("Alpha / Beta", 8, 13, 13, 30),
    ];

    public string SourceDirectory { get => sourceDirectory; private set => SetProperty(ref sourceDirectory, value); }
    public ProjectRecordingRow? SelectedProjectRecording
    {
        get => selectedProjectRecording;
        set
        {
            if (!SetProperty(ref selectedProjectRecording, value)) return;
            TimeDisplay.RecordingStartUtc = value?.RecordingStartUtc;
            SourceDirectory = value?.RecordingDirectory ?? string.Empty;
            RegisteredRecording = null;
            RegisteredChannels.Clear();
            MontageProfiles.Clear();
            SelectedMontageProfile = null;
            SelectedChannel = string.Empty;
            SelectedF4Channel = string.Empty;
            EndSecondsText = string.Empty;
            ClearPreviews();
            if (value is { Status: "已完成" })
                _ = AutoRegisterSelectedRecordingAsync();
        }
    }
    public string SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (!SetProperty(ref selectedChannel, value)) return;
            ScheduleAutomaticRun();
        }
    }
    public string SelectedF4Channel
    {
        get => selectedF4Channel;
        set
        {
            if (!SetProperty(ref selectedF4Channel, value)) return;
            ScheduleAutomaticRun();
        }
    }
    public string StartSecondsText
    {
        get => startSecondsText;
        set
        {
            if (!SetProperty(ref startSecondsText, value)) return;
            ScheduleAutomaticRun();
        }
    }
    public string EndSecondsText
    {
        get => endSecondsText;
        set
        {
            if (!SetProperty(ref endSecondsText, value)) return;
            ScheduleAutomaticRun();
        }
    }
    public RegisteredRecording? RegisteredRecording { get => registeredRecording; private set => SetProperty(ref registeredRecording, value); }
    public AlgorithmCatalogItem? SelectedAlgorithm
    {
        get => selectedAlgorithm;
        set
        {
            if (!SetProperty(ref selectedAlgorithm, value)) return;
            selectedAlgorithmId = value?.Id;
            ClearPreviews();
            SelectedAnalysisMode = value?.Modes.Contains("static", StringComparer.OrdinalIgnoreCase) == true ? "静态" : "动态";
            SelectedDynamicExecutionMode = "全量分析";
            SelectedDynamicWindowSeconds = value?.DynamicPolicy.DefaultWindowSeconds ?? 5.0;
            DynamicStepText = value?.DynamicPolicy.RefreshStepSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "1";
            selectedDynamicStepSeconds = value?.DynamicPolicy.RefreshStepSeconds ?? 1.0;
            ApplyAlgorithmChannelDefaults();
            RaisePropertyChanged(nameof(IsPsdSelected));
            RaisePropertyChanged(nameof(IsStftSelected));
            RaisePropertyChanged(nameof(IsRbpSelected));
            RaisePropertyChanged(nameof(IsPeakFrequencySelected));
            RaisePropertyChanged(nameof(IsBandRatioSelected));
            RaisePropertyChanged(nameof(IsFaaSelected));
            RaisePropertyChanged(nameof(IsIapfSelected));
            RaisePropertyChanged(nameof(IsThetaBetaSelected));
            RaisePropertyChanged(nameof(IsBrainbeatSelected));
            RaisePropertyChanged(nameof(IsTnkSelected));
            RaisePropertyChanged(nameof(ChannelLabel));
            RaisePropertyChanged(nameof(IsDynamicModeAvailable));
            RaisePropertyChanged(nameof(SupportsStaticMode));
            RaisePropertyChanged(nameof(DynamicWindowOptions));
            RaisePropertyChanged(nameof(DynamicStepOptions));
            RaisePropertyChanged(nameof(DynamicExecutionModes));
            RaisePropertyChanged(nameof(IsOnDemandExecutionModeSelected));
            RaisePropertyChanged(nameof(IsCombinedExecutionModeSelected));
            RaisePropertyChanged(nameof(SelectedDynamicStepSeconds));
            ApplyAlgorithmChannelDefaults();
            RefreshTnkRoleChannels();
            ApplyAlgorithmParameterDefaults();
            ScheduleAutomaticRun();
        }
    }
    private bool IsSelected(string id) => string.Equals(SelectedAlgorithm?.Id, id, StringComparison.OrdinalIgnoreCase);
    public bool IsPsdSelected => IsSelected("psd");
    public bool IsStftSelected => IsSelected("stft");
    public bool IsRbpSelected => IsSelected("rbp");
    public bool IsPeakFrequencySelected => IsSelected("peak_frequency");
    public bool IsBandRatioSelected => IsSelected("band_ratio");
    public bool IsFaaSelected => IsSelected("faa");
    public bool IsIapfSelected => IsSelected("iapf");
    public bool IsThetaBetaSelected => IsSelected("theta_beta");
    public bool IsBrainbeatSelected => IsSelected("brainbeat");
    public bool IsTnkSelected => IsSelected("tnk_biofeedback");
    public string ChannelLabel => AlgorithmUiRegistry.For(SelectedAlgorithm?.Id ?? string.Empty).ChannelLabel;
    public string LowFrequencyText { get => lowFrequencyText; set { if (!SetProperty(ref lowFrequencyText, value)) return; if (!applyingBandPreset) SelectedPeakBandPreset = "自定义"; ScheduleAutomaticRun(); } }
    public string HighFrequencyText { get => highFrequencyText; set { if (!SetProperty(ref highFrequencyText, value)) return; if (!applyingBandPreset) SelectedPeakBandPreset = "自定义"; ScheduleAutomaticRun(); } }
    public string SelectedNotchFrequency { get => selectedNotchFrequency; set { if (SetProperty(ref selectedNotchFrequency, value)) ScheduleAutomaticRun(); } }
    public IReadOnlyList<string> NotchFrequencyOptions { get; } = ["关闭", "50 Hz", "60 Hz"];
    public string NumeratorLowFrequencyText { get => numeratorLowFrequencyText; set { if (SetProperty(ref numeratorLowFrequencyText, value)) { if (!applyingBandPreset) SelectedRatioPreset = "自定义"; ScheduleAutomaticRun(); } } }
    public string NumeratorHighFrequencyText { get => numeratorHighFrequencyText; set { if (SetProperty(ref numeratorHighFrequencyText, value)) { if (!applyingBandPreset) SelectedRatioPreset = "自定义"; ScheduleAutomaticRun(); } } }
    public string DenominatorLowFrequencyText { get => denominatorLowFrequencyText; set { if (SetProperty(ref denominatorLowFrequencyText, value)) { if (!applyingBandPreset) SelectedRatioPreset = "自定义"; ScheduleAutomaticRun(); } } }
    public string DenominatorHighFrequencyText { get => denominatorHighFrequencyText; set { if (SetProperty(ref denominatorHighFrequencyText, value)) { if (!applyingBandPreset) SelectedRatioPreset = "自定义"; ScheduleAutomaticRun(); } } }
    public string SelectedPeakBandPreset
    {
        get => selectedPeakBandPreset;
        set
        {
            if (!SetProperty(ref selectedPeakBandPreset, value)) return;
            var preset = FrequencyBandPresets.FirstOrDefault(item => item.Name == value);
            if (preset?.LowHz is double low && preset.HighHz is double high)
            {
                applyingBandPreset = true;
                try
                {
                    LowFrequencyText = low.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    HighFrequencyText = high.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                finally { applyingBandPreset = false; }
            }
        }
    }
    public string SelectedRatioPreset
    {
        get => selectedRatioPreset;
        set
        {
            if (!SetProperty(ref selectedRatioPreset, value)) return;
            var preset = BandRatioPresets.FirstOrDefault(item => item.Name == value);
            if (preset?.NumeratorLowHz is double numeratorLow && preset.NumeratorHighHz is double numeratorHigh &&
                preset.DenominatorLowHz is double denominatorLow && preset.DenominatorHighHz is double denominatorHigh)
            {
                applyingBandPreset = true;
                try
                {
                    NumeratorLowFrequencyText = numeratorLow.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    NumeratorHighFrequencyText = numeratorHigh.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    DenominatorLowFrequencyText = denominatorLow.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    DenominatorHighFrequencyText = denominatorHigh.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                finally { applyingBandPreset = false; }
            }
        }
    }
    public string RunStatusText { get => runStatusText; private set => SetProperty(ref runStatusText, value); }
    public string RunProgressText => LastRun?.ProgressTotal is int total
        ? $"{LastRun.ProgressCompleted}/{total} 窗口"
        : (LastRun?.Status == "running" ? "分析中" : "");
    public string ResultSummaryText { get => resultSummaryText; private set => SetProperty(ref resultSummaryText, value); }
    public string ProvenanceText { get => provenanceText; private set => SetProperty(ref provenanceText, value); }
    public string StructuredPreviewText { get => structuredPreviewText; private set => SetProperty(ref structuredPreviewText, value); }
    public string DynamicSeriesText { get => dynamicSeriesText; private set => SetProperty(ref dynamicSeriesText, value); }
    public AnalysisRunResponse? LastRun { get => lastRun; private set => SetProperty(ref lastRun, value); }
    public bool IsRunActive => isSubmittingRun || windowLoading ||
        LastRun?.Status is "queued" or "running";
    public IReadOnlyList<string> AnalysisModes { get; } = ["静态", "动态"];
    public string SelectedAnalysisMode
    {
        get => selectedAnalysisMode;
        set
        {
            if (value == "静态" && !SupportsStaticMode) value = "动态";
            if (!SetProperty(ref selectedAnalysisMode, value)) return;
            ClearPreviews();
            RaisePropertyChanged(nameof(IsDynamicMode));
            RaisePropertyChanged(nameof(DynamicExecutionModes));
            RaisePropertyChanged(nameof(IsOnDemandExecutionModeSelected));
            RaisePropertyChanged(nameof(IsCombinedExecutionModeSelected));
            ScheduleAutomaticRun();
        }
    }
    public bool IsDynamicMode => SelectedAnalysisMode == "动态";
    public bool SupportsStaticMode => SelectedAlgorithm?.Modes.Contains("static", StringComparer.OrdinalIgnoreCase) == true;
    public IReadOnlyList<string> DynamicExecutionModes
    {
        get
        {
            if (!IsDynamicModeAvailable)
                return [];

            var modes = new List<string> { OfflineAnalysisAvailability.FullOnly };
            if (SelectedAlgorithm is { } algorithm &&
                algorithm.SupportsDeclaredOnDemand &&
                AlgorithmRunResultHandlerRegistry.Contains(algorithm.Id) &&
                AlgorithmRunResultHandlerRegistry.For(algorithm.Id).SupportsOnDemandWindows)
            {
                modes.Add(OfflineAnalysisAvailability.OnDemandOnly);
                if (algorithm.SupportsDeclaredCombined)
                    modes.Add(OfflineAnalysisAvailability.OnDemandWithBackgroundFull);
            }
            return modes;
        }
    }
    public string SelectedDynamicExecutionMode
    {
        get => selectedDynamicExecutionMode;
        set
        {
            var normalized = DynamicExecutionModes.Contains(value, StringComparer.Ordinal)
                ? value
                : OfflineAnalysisAvailability.FullOnly;
            if (!SetProperty(ref selectedDynamicExecutionMode, normalized)) return;
            ClearPreviews();
            RaisePropertyChanged(nameof(IsOnDemandExecutionModeSelected));
            RaisePropertyChanged(nameof(IsCombinedExecutionModeSelected));
            ScheduleAutomaticRun();
        }
    }
    public bool IsOnDemandExecutionModeSelected => IsDynamicMode && SelectedDynamicExecutionMode == OfflineAnalysisAvailability.OnDemandOnly;
    public bool IsCombinedExecutionModeSelected => IsDynamicMode && SelectedDynamicExecutionMode == OfflineAnalysisAvailability.OnDemandWithBackgroundFull;
    public bool IsDynamicPreviewPlaying { get => isDynamicPreviewPlaying; private set => SetProperty(ref isDynamicPreviewPlaying, value); }
    public double DynamicPreviewCursorSeconds { get => dynamicPreviewCursorSeconds; private set => SetProperty(ref dynamicPreviewCursorSeconds, value); }
    public string DynamicPreviewCursorText => TimeDisplay.FormatCursor(DynamicPreviewCursorSeconds);
    public bool HasDynamicPreview => IsOnDemandAnalysis || DynamicWindowRows.Count > 0;
    public string DynamicPreviewSourceText => displayedWindowSource;
    public string DynamicPreviewStatusText => !HasDynamicPreview
        ? "尚未生成动态窗口结果"
        : $"{DynamicPreviewSourceText} · " +
          (IsDynamicPreviewPlaying ? "时间同步预览进行中" :
           DynamicPreviewCursorSeconds >= DynamicPlaybackEndSeconds ? "时间同步预览已结束" :
           "时间同步预览已暂停");
    public ObservableCollection<DynamicWindowRow> DynamicPreviewRows { get; } = [];
    public bool IsDynamicModeAvailable => SelectedAlgorithm?.Modes.Contains("dynamic") == true;
    public IReadOnlyList<double> DynamicWindowOptions => SelectedAlgorithm?.DynamicPolicy.WindowOptionsSeconds ?? [];
    public IReadOnlyList<double> DynamicStepOptions =>
        SelectedAlgorithm?.DynamicPolicy.RefreshStepOptionsSeconds is { Count: > 0 } options
            ? options
            : SelectedAlgorithm is null ? [1.0] : [SelectedAlgorithm.DynamicPolicy.RefreshStepSeconds];
    public double SelectedDynamicWindowSeconds
    {
        get => selectedDynamicWindowSeconds;
        set
        {
            if (!SetProperty(ref selectedDynamicWindowSeconds, value)) return;
            ScheduleAutomaticRun();
        }
    }
    public string DynamicStepText
    {
        get => dynamicStepText;
        set
        {
            if (!SetProperty(ref dynamicStepText, value)) return;
            ScheduleAutomaticRun();
        }
    }
    private double selectedDynamicStepSeconds = 1.0;
    public double SelectedDynamicStepSeconds
    {
        get => selectedDynamicStepSeconds;
        set
        {
            if (!SetProperty(ref selectedDynamicStepSeconds, value)) return;
            DynamicStepText = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    public bool HasRegisteredChannels => RegisteredChannels.Count > 0;

    public IReadOnlyList<string> ModeFilters { get; } = ["全部", "静态", "动态"];
    public IReadOnlyList<string> AvailabilityFilters { get; } = ["全部", "可用", "不可运行", "验证中", "不可用"];
    public IReadOnlyList<string> TypeFilters { get; } = ["全部", "频谱", "时频", "频段功率", "标量"];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (!SetProperty(ref searchText, value)) return;
            ApplyCatalogFilter();
        }
    }

    public string SelectedModeFilter
    {
        get => selectedModeFilter;
        set
        {
            if (!SetProperty(ref selectedModeFilter, value)) return;
            ApplyCatalogFilter();
        }
    }

    public string SelectedAvailabilityFilter
    {
        get => selectedAvailabilityFilter;
        set
        {
            if (!SetProperty(ref selectedAvailabilityFilter, value)) return;
            ApplyCatalogFilter();
        }
    }

    public string SelectedTypeFilter
    {
        get => selectedTypeFilter;
        set
        {
            if (!SetProperty(ref selectedTypeFilter, value)) return;
            ApplyCatalogFilter();
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public bool IsLoadFailed { get => isLoadFailed; private set => SetProperty(ref isLoadFailed, value); }

    private void ReportRunError(Exception exception)
    {
        RunStatusText = exception is AlgorithmApiException apiError
            ? $"算法运行失败：{apiError.Code}：{apiError.Message}"
            : $"算法运行失败：{exception.Message}";
        notifications.PublishError(RunStatusText);
    }

    internal void SetStructuredPreviewTextForModule(string text) => StructuredPreviewText = text;

    internal void SetRunResultTextForModule(string summary, string provenance, string dynamicSeries) =>
        (ResultSummaryText, ProvenanceText, DynamicSeriesText) = (summary, provenance, dynamicSeries);
    internal void SeekDynamicPreviewSecondsForModule(double seconds) => SeekDynamicPreviewSeconds(seconds);
    internal void QueueDynamicPreviewSecondsForPlayback(double seconds)
    {
        if (!IsOnDemandAnalysis || !HasDynamicPreview) return;
        DynamicPreviewCursorSeconds = Math.Clamp(seconds, 0, DynamicPlaybackEndSeconds);
        QueuePlaybackWindowAtCursor();
        RaiseDynamicPreviewProperties();
    }

    public sealed record FrequencyBandPreset(string Name, double? LowHz, double? HighHz);
    public sealed record BandRatioPreset(string Name, double? NumeratorLowHz, double? NumeratorHighHz, double? DenominatorLowHz, double? DenominatorHighHz);

}

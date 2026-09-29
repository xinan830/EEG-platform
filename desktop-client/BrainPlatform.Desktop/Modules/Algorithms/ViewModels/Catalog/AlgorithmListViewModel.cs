using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel : ObservableObject
{
    private readonly IAlgorithmClient client;
    private readonly AlgorithmRunCoordinator runCoordinator;
    private readonly OperationNotificationCenter notifications;
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
    private string selectedChannel = string.Empty;
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

    public AlgorithmListViewModel(IAlgorithmClient client, OperationNotificationCenter notifications, ProjectWorkspaceViewModel? projects = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        runCoordinator = new AlgorithmRunCoordinator(client);
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCatalogError);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, ReportRunError);
        RunCommand = new AsyncRelayCommand(RunAsync, ReportRunError);
        SelectStaticModeCommand = new RelayCommand(() => SelectedAnalysisMode = "静态");
        SelectDynamicModeCommand = new RelayCommand(() => SelectedAnalysisMode = "动态");
        StartDynamicPreviewCommand = new RelayCommand(StartDynamicPreview, CanStartDynamicPreview);
        PauseDynamicPreviewCommand = new RelayCommand(PauseDynamicPreview);
        StepDynamicPreviewCommand = new RelayCommand(StepDynamicPreview, CanStepDynamicPreview);
        ResetDynamicPreviewCommand = new RelayCommand(ResetDynamicPreview, CanResetDynamicPreview);
        dynamicPreviewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        dynamicPreviewTimer.Tick += (_, _) => AdvanceDynamicPreview();
        automaticRunDebounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        automaticRunDebounceTimer.Tick += async (_, _) =>
        {
            automaticRunDebounceTimer.Stop();
            if (!automaticRunPending || IsRunActive || RegisteredRecording is null || SelectedAlgorithm is null)
                return;
            automaticRunPending = false;
            try { await RunAsync(); }
            catch (Exception exception) { ReportRunError(exception); }
        };
        Projects = projects;
    }

    public ObservableCollection<AlgorithmCatalogItem> Items { get; } = [];
    public ObservableCollection<AlgorithmCatalogItem> VisibleItems { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand SelectStaticModeCommand { get; }
    public ICommand SelectDynamicModeCommand { get; }
    public ICommand StartDynamicPreviewCommand { get; }
    public ICommand PauseDynamicPreviewCommand { get; }
    public ICommand StepDynamicPreviewCommand { get; }
    public ICommand ResetDynamicPreviewCommand { get; }
    public ProjectWorkspaceViewModel? Projects { get; }
    public ObservableCollection<string> RegisteredChannels { get; } = [];
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
            SourceDirectory = value?.RecordingDirectory ?? string.Empty;
            RegisteredRecording = null;
            RegisteredChannels.Clear();
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
    public string SelectedF4Channel { get => selectedF4Channel; set => SetProperty(ref selectedF4Channel, value); }
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
            SelectedAnalysisMode = "静态";
            SelectedDynamicWindowSeconds = value?.DynamicPolicy.DefaultWindowSeconds ?? 5.0;
            DynamicStepText = value?.DynamicPolicy.RefreshStepSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "1";
            RaisePropertyChanged(nameof(IsPsdSelected));
            RaisePropertyChanged(nameof(IsStftSelected));
            RaisePropertyChanged(nameof(IsRbpSelected));
            RaisePropertyChanged(nameof(IsPeakFrequencySelected));
            RaisePropertyChanged(nameof(IsBandRatioSelected));
            RaisePropertyChanged(nameof(IsFaaSelected));
            RaisePropertyChanged(nameof(IsIapfSelected));
            RaisePropertyChanged(nameof(IsThetaBetaSelected));
            RaisePropertyChanged(nameof(ChannelLabel));
            RaisePropertyChanged(nameof(IsDynamicModeAvailable));
            RaisePropertyChanged(nameof(DynamicWindowOptions));
            ScheduleAutomaticRun();
        }
    }
    public bool IsPsdSelected => SelectedAlgorithm?.Id == "psd";
    public bool IsStftSelected => SelectedAlgorithm?.Id == "stft";
    public bool IsRbpSelected => SelectedAlgorithm?.Id == "rbp";
    public bool IsPeakFrequencySelected => SelectedAlgorithm?.Id == "peak_frequency";
    public bool IsBandRatioSelected => SelectedAlgorithm?.Id == "band_ratio";
    public bool IsFaaSelected => SelectedAlgorithm?.Id == "faa";
    public bool IsIapfSelected => SelectedAlgorithm?.Id == "iapf";
    public bool IsThetaBetaSelected => SelectedAlgorithm?.Id == "theta_beta";
    public string ChannelLabel => IsFaaSelected ? "FAA F3 通道" : IsStftSelected ? "STFT 通道" : IsRbpSelected ? "RBP 通道" : IsPeakFrequencySelected ? "峰频率通道" : IsBandRatioSelected ? "频段比通道" : IsIapfSelected ? "IAPF 通道" : IsThetaBetaSelected ? "Theta/Beta 通道" : "PSD 通道";
    public string LowFrequencyText { get => lowFrequencyText; set { if (!SetProperty(ref lowFrequencyText, value)) return; if (!applyingBandPreset) SelectedPeakBandPreset = "自定义"; ScheduleAutomaticRun(); } }
    public string HighFrequencyText { get => highFrequencyText; set { if (!SetProperty(ref highFrequencyText, value)) return; if (!applyingBandPreset) SelectedPeakBandPreset = "自定义"; ScheduleAutomaticRun(); } }
    public string SelectedNotchFrequency { get => selectedNotchFrequency; set { if (SetProperty(ref selectedNotchFrequency, value)) ScheduleAutomaticRun(); } }
    public IReadOnlyList<string> NotchFrequencyOptions { get; } = ["关闭", "50 Hz", "60 Hz"];
    public string NumeratorLowFrequencyText { get => numeratorLowFrequencyText; set { if (SetProperty(ref numeratorLowFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string NumeratorHighFrequencyText { get => numeratorHighFrequencyText; set { if (SetProperty(ref numeratorHighFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string DenominatorLowFrequencyText { get => denominatorLowFrequencyText; set { if (SetProperty(ref denominatorLowFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string DenominatorHighFrequencyText { get => denominatorHighFrequencyText; set { if (SetProperty(ref denominatorHighFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
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
    public string ResultSummaryText { get => resultSummaryText; private set => SetProperty(ref resultSummaryText, value); }
    public string ProvenanceText { get => provenanceText; private set => SetProperty(ref provenanceText, value); }
    public string StructuredPreviewText { get => structuredPreviewText; private set => SetProperty(ref structuredPreviewText, value); }
    public string DynamicSeriesText { get => dynamicSeriesText; private set => SetProperty(ref dynamicSeriesText, value); }
    public AnalysisRunResponse? LastRun { get => lastRun; private set => SetProperty(ref lastRun, value); }
    public bool IsRunActive => LastRun?.Status is "queued" or "running";
    public IReadOnlyList<string> AnalysisModes { get; } = ["静态", "动态"];
    public string SelectedAnalysisMode
    {
        get => selectedAnalysisMode;
        set
        {
            if (!SetProperty(ref selectedAnalysisMode, value)) return;
            ClearPreviews();
            RaisePropertyChanged(nameof(IsDynamicMode));
            RaisePropertyChanged(nameof(RbpCardTitle));
            ScheduleAutomaticRun();
        }
    }
    public bool IsDynamicMode => SelectedAnalysisMode == "动态";
    public bool IsDynamicPreviewPlaying { get => isDynamicPreviewPlaying; private set => SetProperty(ref isDynamicPreviewPlaying, value); }
    public double DynamicPreviewCursorSeconds { get => dynamicPreviewCursorSeconds; private set => SetProperty(ref dynamicPreviewCursorSeconds, value); }
    public string DynamicPreviewCursorText => $"{DynamicPreviewCursorSeconds:0.###} s";
    public bool HasDynamicPreview => DynamicWindowRows.Count > 0;
    public string DynamicPreviewStatusText => !HasDynamicPreview
        ? "尚未生成动态窗口结果"
        : IsDynamicPreviewPlaying ? "时间同步预览进行中" : DynamicPreviewCursorSeconds >= DynamicWindowRows.Max(row => row.EndSeconds) ? "时间同步预览已结束" : "时间同步预览已暂停";
    public ObservableCollection<DynamicWindowRow> DynamicPreviewRows { get; } = [];
    public string RbpCardTitle => IsDynamicMode ? "相对频段功率（最近有效窗口）" : "相对频段功率";
    public bool IsDynamicModeAvailable => SelectedAlgorithm?.Modes.Contains("dynamic") == true;
    public IReadOnlyList<double> DynamicWindowOptions => SelectedAlgorithm?.DynamicPolicy.WindowOptionsSeconds ?? [];
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

    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        IsLoadFailed = false;
        try
        {
            var items = await client.ListAlgorithmsAsync(CancellationToken.None);
            // Synchronize in place. Clearing the collection causes WPF to emit a
            // transient SelectedItem=null and rebuild the whole ListView. That
            // is visible as a selection flash when refresh overlaps a click.
            // Keeping existing rows (and moving them into catalog order) avoids
            // that transient state while still removing stale algorithms.
            var incomingIds = items.Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var index = Items.Count - 1; index >= 0; index--)
            {
                if (!incomingIds.Contains(Items[index].Id))
                    Items.RemoveAt(index);
            }

            for (var index = 0; index < items.Count; index++)
            {
                var incoming = items[index];
                var existingIndex = -1;
                for (var candidateIndex = 0; candidateIndex < Items.Count; candidateIndex++)
                {
                    if (string.Equals(Items[candidateIndex].Id, incoming.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIndex = candidateIndex;
                        break;
                    }
                }

                if (existingIndex < 0)
                {
                    Items.Insert(index, incoming);
                    continue;
                }

                if (existingIndex != index)
                    Items.Move(existingIndex, index);

                if (!Equals(Items[index], incoming) &&
                    !string.Equals(incoming.Id, selectedAlgorithmId, StringComparison.OrdinalIgnoreCase))
                {
                    Items[index] = incoming;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedAlgorithmId))
                SelectedAlgorithm = Items.FirstOrDefault(item => string.Equals(item.Id, selectedAlgorithmId, StringComparison.OrdinalIgnoreCase));
            ApplyCatalogFilter();
            StatusText = $"已加载 {Items.Count} 个官方算法。";
        }
        catch (Exception exception)
        {
            IsLoadFailed = true;
            ReportCatalogError(exception);
        }
        finally { IsLoading = false; }
    }

    private void ApplyCatalogFilter()
    {
        var query = SearchText.Trim();
        var filtered = Items.Where(item =>
            (query.Length == 0 ||
             item.DisplayNameZh.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedModeFilter == "全部" ||
             (SelectedModeFilter == "静态" && item.Modes.Contains("static")) ||
             (SelectedModeFilter == "动态" && item.Modes.Contains("dynamic"))) &&
            (SelectedAvailabilityFilter == "全部" ||
             item.StatusDisplay == SelectedAvailabilityFilter) &&
            (SelectedTypeFilter == "全部" || item.AlgorithmTypeDisplay == SelectedTypeFilter))
            .ToArray();

        VisibleItems.Clear();
        foreach (var item in filtered)
            VisibleItems.Add(item);

        if (SelectedAlgorithm is not null && !VisibleItems.Contains(SelectedAlgorithm))
            SelectedAlgorithm = null;
        RaisePropertyChanged(nameof(VisibleItems));
    }

    private void ReportCatalogError(Exception exception)
    {
        StatusText = exception is AlgorithmApiException apiError
            ? $"算法目录加载失败：{apiError.Code}"
            : $"算法目录加载失败：{exception.Message}";
        notifications.PublishError(StatusText);
    }

    private void ReportRunError(Exception exception)
    {
        RunStatusText = exception is AlgorithmApiException apiError
            ? $"算法运行失败：{apiError.Code}：{apiError.Message}"
            : $"算法运行失败：{exception.Message}";
        notifications.PublishError(RunStatusText);
    }

    private async Task AutoRegisterSelectedRecordingAsync()
    {
        try
        {
            await RegisterAsync();
            ScheduleAutomaticRun();
        }
        catch (Exception exception)
        {
            ReportRunError(exception);
        }
    }

    private void ScheduleAutomaticRun()
    {
        if (SelectedProjectRecording is not { Status: "已完成" } ||
            RegisteredRecording is null ||
            SelectedAlgorithm is null ||
            string.IsNullOrWhiteSpace(SelectedChannel))
            return;

        automaticRunPending = true;
        automaticRunDebounceTimer.Stop();
        automaticRunDebounceTimer.Start();
    }

    private async Task RegisterAsync()
    {
        ResultSummaryText = "暂无结果摘要。";
        ProvenanceText = "暂无溯源信息。";
        StructuredPreviewText = "暂无结构化预览。";
        DynamicSeriesText = "暂无动态结果。";
        ClearPreviews();
        if (SelectedProjectRecording is not { Status: "已完成" } || string.IsNullOrWhiteSpace(SourceDirectory))
        {
            throw new InvalidOperationException("请先从项目中选择一条已完成的记录。");
        }

        RegisteredRecording = await runCoordinator.RegisterRecordingAsync(SourceDirectory.Trim(), CancellationToken.None);
        if (RegisteredRecording.Channels.Count == 0 || RegisteredRecording.DurationSeconds is not > 0)
        {
            throw new InvalidOperationException("注册记录没有有效的通道或时长。");
        }
        RegisteredChannels.Clear();
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        foreach (var channel in RegisteredRecording.Channels
                     .Where(channel => !string.IsNullOrWhiteSpace(channel))
                     .Select(channel => channel.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RegisteredChannels.Add(channel);
        }
        if (RegisteredChannels.Count == 0)
            throw new InvalidOperationException("注册记录没有可分析的 EEG 通道。");
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        SelectedChannel = RegisteredChannels[0];
        SelectedF4Channel = RegisteredChannels.Count > 1 ? RegisteredChannels[1] : string.Empty;
        EndSecondsText = RegisteredRecording.DurationSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        StatusText = $"已注册记录：{RegisteredRecording.OriginalName}，{RegisteredRecording.DurationSeconds.Value:0.###} 秒。请选择分析通道和范围。";
    }

    private async Task RunAsync()
    {
        automaticRunPending = false;
        automaticRunDebounceTimer.Stop();
        var algorithm = SelectedAlgorithm ?? throw new InvalidOperationException("请选择一个官方算法。");
        if (!AlgorithmRunConfiguration.IsSupportedAlgorithm(algorithm))
        {
            throw new InvalidOperationException("当前算法不可运行，或未声明可用的分析模式。");
        }
        var dynamic = IsDynamicMode;
        if (dynamic && !IsDynamicModeAvailable)
            throw new InvalidOperationException("当前算法未声明动态分析模式。");
        if (RegisteredRecording is null)
        {
            throw new InvalidOperationException("请先注册一条已完成的 WPF 记录。");
        }
        if (string.IsNullOrWhiteSpace(SelectedChannel) || !RegisteredChannels.Contains(SelectedChannel))
            throw new InvalidOperationException("请选择注册记录返回的有效分析通道。");
        if (algorithm.Id == "faa" &&
            (string.IsNullOrWhiteSpace(SelectedF4Channel) || !RegisteredChannels.Contains(SelectedF4Channel)))
            throw new InvalidOperationException("请选择注册记录返回的有效 FAA F4 来源通道。");
        if (algorithm.Id == "faa" && string.Equals(SelectedChannel, SelectedF4Channel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FAA 的 F3 与 F4 来源通道必须不同。");
        var durationSeconds = RegisteredRecording.DurationSeconds ?? throw new InvalidOperationException("注册记录没有可用的时长。");
        var requestedRange = AlgorithmRunConfiguration.ParseStaticRangeOrThrow(StartSecondsText, EndSecondsText, durationSeconds);
        if (algorithm.Id == "stft" && requestedRange.EndSeconds - requestedRange.StartSeconds < 4.0)
            throw new InvalidOperationException("STFT 分析区间至少需要 4 秒。");
        var frequencyBand = algorithm.Id is "peak_frequency" or "psd"
            ? AlgorithmRunConfiguration.ParseFrequencyBandOrThrow(LowFrequencyText, HighFrequencyText, RegisteredRecording.SamplingRateHz)
            : null;
        var ratioBands = algorithm.Id == "band_ratio"
            ? AlgorithmRunConfiguration.ParseBandRatioBandsOrThrow(
                NumeratorLowFrequencyText, NumeratorHighFrequencyText,
                DenominatorLowFrequencyText, DenominatorHighFrequencyText,
                RegisteredRecording.SamplingRateHz)
             : null;
        var stepSeconds = dynamic ? AlgorithmRunConfiguration.ParsePositiveSecondsOrThrow(DynamicStepText, "刷新步长") : 0;
        if (dynamic && SelectedDynamicWindowSeconds < algorithm.DynamicPolicy.MinimumWindowSeconds)
            throw new InvalidOperationException($"动态窗口不能小于 {algorithm.DynamicPolicy.MinimumWindowSeconds:0.###} 秒。");
        var preservedDynamicCursorSeconds = dynamic && HasDynamicPreview
            ? DynamicPreviewCursorSeconds
            : (double?)null;
        ClearPreviews();
        var start = requestedRange.StartSeconds;
        var end = requestedRange.EndSeconds;
        var notchHz = algorithm.Id == "psd" && SelectedNotchFrequency != "关闭"
            ? (SelectedNotchFrequency.StartsWith("60", StringComparison.Ordinal) ? 60.0 : 50.0)
            : (double?)0.0;
        var config = AlgorithmRunConfiguration.BuildRunConfig(algorithm, SelectedChannel, requestedRange, dynamic ? "dynamic" : "static", frequencyBand, ratioBands, algorithm.Id == "faa" ? SelectedF4Channel : null, dynamic ? SelectedDynamicWindowSeconds : null, dynamic ? stepSeconds : null, notchHz);
        var request = new AnalysisRunRequest(RegisteredRecording.Id, "official_algorithm", config);
        var run = await runCoordinator.CreateRunAndWaitAsync(
            request,
            updatedRun =>
            {
                LastRun = updatedRun;
                RunStatusText = $"{algorithm.DisplayNameZh}：{updatedRun.Status}（{updatedRun.RunId}）";
                RaisePropertyChanged(nameof(IsRunActive));
            },
            CancellationToken.None);
        LastRun = run;
        RaisePropertyChanged(nameof(IsRunActive));
        if (run.Status == "completed")
        {
            RunStatusText += "，结果已由后端保存。";
            ResultSummaryText = AlgorithmResultFormatter.BuildResultSummary(run);
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            DynamicSeriesText = dynamic ? AlgorithmResultFormatter.BuildDynamicSeriesSummary(run) : "暂无动态结果。";
            if (dynamic && algorithm.Id is not "psd" and not "stft")
                SetDynamicSeries(run);
            if (algorithm.Id == "rbp" && !dynamic)
            {
                UpdateRbpValues(run);
                StructuredPreviewText = "RBP 为四频段标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "rbp" && dynamic)
            {
                SetDynamicSeries(run);
                UpdateDynamicRbpValues(run);
                StructuredPreviewText = "动态 RBP 每个窗口返回 Delta、Theta、Alpha、Beta 相对功率，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "peak_frequency")
            {
                StructuredPreviewText = "频段峰频率为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "band_ratio")
            {
                StructuredPreviewText = "频段功率比为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "faa")
            {
                StructuredPreviewText = "额叶 Alpha 不对称性为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "iapf")
            {
                StructuredPreviewText = "个体 Alpha 峰频率为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "theta_beta")
            {
                StructuredPreviewText = "Theta/Beta 比值为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else
            {
                await LoadStructuredPreviewAsync(run.RunId, algorithm.Id, dynamic);
                if (dynamic && preservedDynamicCursorSeconds is double cursorSeconds)
                    SeekDynamicPreviewSeconds(cursorSeconds);
            }
        }
        else if (run.Error is not null)
        {
            RunStatusText += $"，{run.Error.Code}：{run.Error.Message}";
            ResultSummaryText = $"结果不可用：{run.Error.Code}。{run.Error.Message}";
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
        else
        {
            ResultSummaryText = $"结果不可用：运行状态为 {run.Status}。";
            ProvenanceText = AlgorithmResultFormatter.BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
    }

    public sealed record FrequencyBandPreset(string Name, double? LowHz, double? HighHz);
    public sealed record BandRatioPreset(string Name, double? NumeratorLowHz, double? NumeratorHighHz, double? DenominatorLowHz, double? DenominatorHighHz);

}

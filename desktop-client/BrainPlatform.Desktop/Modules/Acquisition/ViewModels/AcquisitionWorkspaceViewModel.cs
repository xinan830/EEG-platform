using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed record AcquisitionChannelRow(int StreamIndex, int NativeChannelIndex, string Role, string Label, string Unit);

public sealed class AcquisitionWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly DeviceSessionManager deviceSession;
    private readonly IAcquisitionSettingsStore settingsStore;
    private readonly Dispatcher dispatcher;
    private readonly RecordingHistoryViewModel recordingHistory;
    private readonly ChannelMappingViewModel channelMapping;
    private readonly OperationNotificationCenter? notifications;
    private readonly EventDefinitionService eventDefinitionService;
    private string sdkLibraryPath;
    private string recordingDirectory;
    private int? savedSamplingRateHz;
    private string savedReferenceRangeText;
    private string savedBipolarRangeText;
    private IReadOnlyList<double> availableBipolarRanges = [];
    private int? selectedSamplingRateHz;
    private double? selectedReferenceRangeVolts;
    private double? selectedBipolarRangeVolts;
    private MontageProfile? selectedMontageProfile;
    private ResearchProject? selectedProject;
    private string acquisitionStatusText = "尚未连接放大器。";
    private string operationMessage = "打开“放大器设置”，选择 SDK 文件后测试连接。";
    private double? automaticReferenceRangeVolts;
    private double? automaticBipolarRangeVolts;
    private bool isEditingInputRanges;
    private bool isManualInputRangeSelection;
    private bool isPreparationStartInProgress;
    private bool disposed;
    private string? selectedEventDefinitionId;

    public AcquisitionWorkspaceViewModel(
        IAcquisitionSettingsStore? settingsStore = null,
        ConfiguredAcquisitionRuntime? runtime = null,
        DeviceSessionManager? deviceSession = null,
        Dispatcher? dispatcher = null,
        OperationNotificationCenter? notifications = null,
        EventDefinitionService? eventDefinitionService = null)
    {
        this.settingsStore = settingsStore ?? new LocalAcquisitionSettingsStore();
        this.deviceSession = deviceSession ?? new DeviceSessionManager(runtime ?? new ConfiguredAcquisitionRuntime());
        this.runtime = this.deviceSession.Runtime;
        if (runtime is not null && !ReferenceEquals(runtime, this.runtime))
        {
            throw new ArgumentException("设备会话必须使用同一个采集运行时。", nameof(deviceSession));
        }
        this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        this.notifications = notifications;
        this.eventDefinitionService = eventDefinitionService ?? new EventDefinitionService(new EventDefinitionStore());
        var settings = LoadSettings();
        sdkLibraryPath = settings.SdkLibraryPath;
        recordingDirectory = settings.RecordingDirectory;
        savedSamplingRateHz = settings.SamplingRateHz;
        savedReferenceRangeText = settings.ReferenceRangeText;
        savedBipolarRangeText = settings.BipolarRangeText;
        recordingHistory = new RecordingHistoryViewModel(new LocalRecordingCatalog());
        recordingHistory.Refresh(recordingDirectory);
        channelMapping = new ChannelMappingViewModel(notifications: notifications);
        LiveMonitor = new LiveMonitoringViewModel(
            () => this.runtime.State,
            () => this.runtime.StreamMetadata,
            () => this.runtime.GetDisplaySnapshot(),
            this.dispatcher,
            () => this.runtime.GetDisplayFilterBoundaries(),
            () => this.runtime.RecordingFirstSampleCounter,
            () => LiveRecordingEvents.ToArray(),
            () => this.runtime.RecordingStartUtc);
        DisplayPreferences = new LiveDisplayPreferencesViewModel(
            this.runtime,
            LiveMonitor,
            new LiveDisplayPreferences(
                settings.LiveHighPassHz,
                settings.LiveLowPassHz,
                settings.LiveNotchHz,
                settings.PaperSpeedMillimetersPerSecond,
                settings.HorizontalTimeScaleMode,
                settings.TimebaseSecondsPerScreen),
            SaveDisplayPreferencesAsync);
        DisplayPreferences.MessageRaised += OnDisplayPreferenceMessage;
        channelMapping.DisplayChannelsChanged += OnDisplayChannelsChanged;
        this.deviceSession.PropertyChanged += OnDeviceSessionPropertyChanged;
        this.runtime.StateChanged += OnRuntimeStateChanged;
        this.runtime.AnalysisFaulted += OnAnalysisFaulted;

        BrowseSdkLibraryCommand = new AsyncRelayCommand(BrowseSdkLibraryAsync, ReportCommandError);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionCoreAsync, ReportCommandError);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingFromPreviewCoreAsync, ReportCommandError);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingCoreAsync, ReportCommandError);
        PauseRecordingCommand = new AsyncRelayCommand(PauseRecordingCoreAsync, ReportCommandError);
        ResumeRecordingCommand = new AsyncRelayCommand(ResumeRecordingCoreAsync, ReportCommandError);
        RefreshRecordingsCommand = new AsyncRelayCommand(RefreshRecordingsAsync, ReportCommandError);
        SaveChannelMappingCommand = new AsyncRelayCommand(SaveChannelMappingAsync, ReportCommandError);
        BeginInputRangeEditCommand = new AsyncRelayCommand(() =>
        {
            if (CanEditInputRanges)
            {
                IsEditingInputRanges = true;
            }
            return Task.CompletedTask;
        }, ReportCommandError);
        CancelInputRangeEditCommand = new AsyncRelayCommand(() =>
        {
            SelectedReferenceRangeVolts = automaticReferenceRangeVolts;
            SelectedBipolarRangeVolts = automaticBipolarRangeVolts;
            IsEditingInputRanges = false;
            isManualInputRangeSelection = false;
            RaiseInputRangePropertiesChanged();
            return Task.CompletedTask;
        }, ReportCommandError);
        ConfirmInputRangeEditCommand = new AsyncRelayCommand(() =>
        {
            if (SelectedReferenceRangeVolts is not { } referenceRange ||
                SelectedBipolarRangeVolts is not { } bipolarRange ||
                !AntEegoRangePair.IsCompatible(referenceRange, bipolarRange))
            {
                throw new InvalidOperationException("请选择一组设备支持且彼此兼容的输入量程。");
            }

            isManualInputRangeSelection = true;
            IsEditingInputRanges = false;
            RaiseInputRangePropertiesChanged();
            return Task.CompletedTask;
        }, ReportCommandError);
        MarkSelectedEventCommand = new AsyncRelayCommand(MarkSelectedEventAsync, ReportCommandError);
        if (!string.IsNullOrWhiteSpace(sdkLibraryPath))
        {
            _ = InitializeDeviceAsync();
        }
    }

    public ReadOnlyObservableCollection<AcquisitionDeviceDescriptor> Devices => deviceSession.Devices;

    public ObservableCollection<int> SamplingRatesHz { get; } = [];

    public ObservableCollection<double> ReferenceRangesVolts { get; } = [];

    public ObservableCollection<double> BipolarRangesVolts { get; } = [];

    public ObservableCollection<AcquisitionChannelRow> Channels { get; } = [];

    public ObservableCollection<EventDefinition> EnabledEventDefinitions { get; } = [];

    /// <summary>
    /// Events already durably written for the active recording. The live canvas
    /// reads this in-memory projection; it never polls the event file on a
    /// render tick.
    /// </summary>
    public ObservableCollection<RecordingEvent> LiveRecordingEvents { get; } = [];

    public RecordingHistoryViewModel RecordingHistory => recordingHistory;

    public ChannelMappingViewModel ChannelMapping => channelMapping;

    public DeviceSessionManager DeviceSession => deviceSession;

    public LiveMonitoringViewModel LiveMonitor { get; }

    public LiveDisplayPreferencesViewModel DisplayPreferences { get; }

    public ICommand BrowseSdkLibraryCommand { get; }

    public ICommand TestConnectionCommand { get; }

    public ICommand StartRecordingCommand { get; }

    public ICommand StopRecordingCommand { get; }

    public ICommand PauseRecordingCommand { get; }

    public ICommand ResumeRecordingCommand { get; }

    public ICommand RefreshRecordingsCommand { get; }

    public ICommand SaveChannelMappingCommand { get; }

    public ICommand BeginInputRangeEditCommand { get; }

    public ICommand CancelInputRangeEditCommand { get; }

    public ICommand ConfirmInputRangeEditCommand { get; }

    public ICommand MarkSelectedEventCommand { get; }

    public string? SelectedEventDefinitionId
    {
        get => selectedEventDefinitionId;
        set => SetProperty(ref selectedEventDefinitionId, value);
    }

    public EventDefinitionService EventDefinitionService => eventDefinitionService;

    public string SdkLibraryPath
    {
        get => sdkLibraryPath;
        set
        {
            if (runtime.State.State is AcquisitionState.Starting or AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping)
            {
                OperationMessage = "采集运行期间不能更换设备 SDK。请先停止采集。";
                return;
            }

            if (SetProperty(ref sdkLibraryPath, value))
            {
                InvalidateConnectionTest();
            }
        }
    }

    public string RecordingDirectory
    {
        get => recordingDirectory;
        set => SetProperty(ref recordingDirectory, value);
    }

    public AcquisitionDeviceDescriptor? SelectedDevice
    {
        get => deviceSession.SelectedDevice;
        set => deviceSession.SelectDevice(value);
    }

    public MontageProfile? SelectedMontageProfile
    {
        get => selectedMontageProfile;
        set
        {
            if (SetProperty(ref selectedMontageProfile, value))
            {
                RaisePropertyChanged(nameof(SelectedMontageChannelConfigurationName));
                RaisePropertyChanged(nameof(SelectedMontageOutputChannelCount));
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public ResearchProject? SelectedProject
    {
        get => selectedProject;
        set
        {
            if ((runtime.State.State is AcquisitionState.Starting or AcquisitionState.Previewing or AcquisitionState.Recording or
                 AcquisitionState.Paused or AcquisitionState.Stopping) && value?.Id != selectedProject?.Id)
            {
                OperationMessage = "采集会话运行期间不能切换项目。";
                return;
            }

            if (SetProperty(ref selectedProject, value))
            {
                recordingHistory.Refresh(value?.RecordingsDirectory ?? string.Empty);
                RaiseProjectSummaryPropertiesChanged();
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public int? SelectedSamplingRateHz
    {
        get => selectedSamplingRateHz;
        set
        {
            if (SetProperty(ref selectedSamplingRateHz, value))
            {
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public double? SelectedReferenceRangeVolts
    {
        get => selectedReferenceRangeVolts;
        set
        {
            if (SetProperty(ref selectedReferenceRangeVolts, value))
            {
                RefreshBipolarRangeChoices();
                RaiseInputRangePropertiesChanged();
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public double? SelectedBipolarRangeVolts
    {
        get => selectedBipolarRangeVolts;
        set
        {
            if (SetProperty(ref selectedBipolarRangeVolts, value))
            {
                RaiseInputRangePropertiesChanged();
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public DeviceReadiness DeviceState
    {
        get => deviceSession.Readiness;
    }

    // Preparation-page summaries deliberately expose only facts reported by
    // the device session and selected project. They do not infer capabilities
    // from a montage or from fixed ANT assumptions.
    public string ProjectNameText => SelectedProject?.Name ?? "未选择项目";

    public string ProjectNumberText => SelectedProject?.Number ?? "未生成";

    public string ProjectDirectoryText => SelectedProject?.DirectoryPath ?? "未配置目录";

    public bool HasSelectedProject => SelectedProject is not null;

    // Adapter readiness answers whether a driver implementation is available;
    // it is not a physical connection fact. ANT reports "待测试" even after
    // SDK discovery succeeds, so this UI must use the session's published
    // discovery/stream state instead.
    public string DeviceConnectionStatusText => deviceSession.Snapshot.State switch
    {
        DeviceSessionState.Connected => "已连接",
        DeviceSessionState.Streaming => "数据流已打开",
        DeviceSessionState.Discovering => "检测中",
        DeviceSessionState.Faulted => "连接异常",
        _ => "未连接",
    };

    public bool IsDeviceConnected => deviceSession.Snapshot.State is
        DeviceSessionState.Connected or DeviceSessionState.Streaming;

    public string DeviceVendorText => DescribeDeviceVendor(SelectedDevice?.DriverId ?? deviceSession.Snapshot.DriverId);

    public string DeviceModelText => SelectedDevice?.Model ?? "SDK 未报告型号";

    public string DeviceSerialNumberText => SelectedDevice?.SerialNumber
        ?? SelectedDevice?.DeviceInstanceId
        ?? "SDK 未报告序列号";

    public string DeviceInputCapabilitySummary => SelectedDevice is null
        ? "设备连接后读取"
        : $"参考输入 {ReferenceInputCount} 路，双极输入 {BipolarInputCount} 路；{OtherInputCapabilitiesText}";

    public bool IsEditingInputRanges
    {
        get => isEditingInputRanges;
        private set
        {
            if (SetProperty(ref isEditingInputRanges, value))
            {
                RaiseInputRangePropertiesChanged();
            }
        }
    }

    public bool CanEditInputRanges =>
        deviceSession.IsReadyForSetup && runtime.State.State is not AcquisitionState.Starting and not AcquisitionState.Previewing and
        not AcquisitionState.Recording and not AcquisitionState.Paused and not AcquisitionState.Stopping &&
        ReferenceRangesVolts.Count > 0 && BipolarRangesVolts.Count > 0;

    public bool CanConfirmInputRangeEdit =>
        IsEditingInputRanges &&
        SelectedReferenceRangeVolts is { } referenceRange &&
        SelectedBipolarRangeVolts is { } bipolarRange &&
        AntEegoRangePair.IsCompatible(referenceRange, bipolarRange);

    public string InputRangeSelectionModeText => isManualInputRangeSelection ? "手动设置" : "自动选择";

    public string ReferenceInputRangeText => FormatInputRange(SelectedReferenceRangeVolts);

    public string BipolarInputRangeText => FormatInputRange(SelectedBipolarRangeVolts);

    public string SupportedSamplingRatesText => SamplingRatesHz.Count == 0
        ? "设备连接后读取"
        : string.Join("、", SamplingRatesHz) + " Hz";

    public int ReferenceInputCount => CountInputChannels(AcquisitionChannelKind.Reference);

    public int BipolarInputCount => CountInputChannels(AcquisitionChannelKind.Bipolar);

    public string OtherInputCapabilitiesText
    {
        get
        {
            var capabilities = SelectedDevice?.ChannelCapabilities;
            if (capabilities is not { Count: > 0 }) return "设备连接后读取";

            var groups = capabilities
                .Where(capability => capability.Kind is not AcquisitionChannelKind.Reference and not AcquisitionChannelKind.Bipolar and not AcquisitionChannelKind.Unknown)
                .GroupBy(capability => capability.Kind)
                .OrderBy(group => group.Key)
                .Select(group => $"{DescribeInputKind(group.Key)} {group.Count()} 路")
                .ToArray();
            return groups.Length == 0 ? "无其他输入" : string.Join("、", groups);
        }
    }

    public string SelectedMontageChannelConfigurationName => SelectedMontageProfile?.ChannelConfigurationName ?? "未选择";

    public int SelectedMontageOutputChannelCount => SelectedMontageProfile?.ChannelCount ?? 0;

    public string AcquisitionStatusText
    {
        get => acquisitionStatusText;
        private set
        {
            if (SetProperty(ref acquisitionStatusText, value))
            {
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public string OperationMessage
    {
        get => operationMessage;
        private set => SetProperty(ref operationMessage, value);
    }

    public bool CanTestConnection => runtime.State.State is not AcquisitionState.Previewing and not AcquisitionState.Recording and not AcquisitionState.Paused and not AcquisitionState.Starting and not AcquisitionState.Stopping;

    public bool CanStart => deviceSession.IsReadyForSetup && SelectedSamplingRateHz is > 0 &&
        SamplingRatesHz.Contains(SelectedSamplingRateHz.Value) &&
        SelectedReferenceRangeVolts is { } referenceRange && SelectedBipolarRangeVolts is { } bipolarRange &&
        AntEegoRangePair.IsCompatible(referenceRange, bipolarRange) &&
        SelectedMontageProfile is not null && SelectedProject is not null && !IsPreparationStartInProgress;

    public bool IsPreparationStartInProgress
    {
        get => isPreparationStartInProgress;
        private set
        {
            if (SetProperty(ref isPreparationStartInProgress, value))
            {
                RaiseCommandAvailabilityChanged();
            }
        }
    }

    public bool CanStartRecording => runtime.State.State == AcquisitionState.Previewing;

    public bool CanStop => runtime.State.State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Starting or AcquisitionState.Stopping;

    public bool CanPause => runtime.State.State == AcquisitionState.Recording;

    public bool CanResume => runtime.State.State == AcquisitionState.Paused;

    public async Task<RecordingEvent> MarkEventAsync(
        string definitionId,
        EventSource source = EventSource.ManualButton,
        string? sourceDetail = null,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State.State != AcquisitionState.Recording)
            throw new InvalidOperationException("只有正在记录时才能添加事件标记。");

        // Capture the coordinate before scheduling or awaiting persistence.
        var recordingId = runtime.RecordingSessionId?.ToString("N")
            ?? throw new InvalidOperationException("当前没有活动 Recording。");
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
        var directory = runtime.RecordingDirectory
            ?? throw new InvalidOperationException("当前 Recording 目录不可用。");
        var service = new RecordingEventService(
            recordingId,
            new RecordingEventStore(directory),
            eventDefinitionService);
        var item = await service.CreateAsync(
            definitionId, coordinate.RecordingRelativeSample, 0, source, sourceDetail, null, null, cancellationToken,
            sourceSampleCounter: coordinate.SourceSampleCounter,
            coordinateStatus: coordinate.Status,
            coordinateUnavailableReason: coordinate.UnavailableReason);
        LiveRecordingEvents.Add(item);
        var recordingStartUtc = runtime.RecordingStartUtc;
        var samplingRateHz = runtime.StreamMetadata?.SamplingRateHz;
        var markerTime = recordingStartUtc is { } start && samplingRateHz is > 0
            ? RecordingEventDisplayTime.FormatClockTime(start, item.StartSample, samplingRateHz.Value)
            : $"第 {item.StartSample} 个采样点";
        var message = $"事件“{item.DefinitionSnapshot.Name}”已标记在记录时间 {markerTime}。";
        OperationMessage = message;
        notifications?.PublishSuccess(message);
        return item;
    }

    public async Task RefreshEventDefinitionsAsync()
    {
        var definitions = await eventDefinitionService.ListAsync(CancellationToken.None);
        EnabledEventDefinitions.Clear();
        foreach (var definition in definitions.Where(item => item.IsEnabled).OrderBy(item => item.Name, StringComparer.CurrentCulture))
            EnabledEventDefinitions.Add(definition);
        SelectedEventDefinitionId ??= EnabledEventDefinitions.FirstOrDefault()?.Id;
        RaisePropertyChanged(nameof(EnabledEventDefinitions));
    }

    private async Task MarkSelectedEventAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedEventDefinitionId))
            throw new InvalidOperationException("请先选择一个事件定义。");
        await MarkEventAsync(SelectedEventDefinitionId, EventSource.ManualButton, "acquisition-toolbar");
    }

    public async Task StartPreparedPreviewAsync()
    {
        if (IsPreparationStartInProgress)
        {
            return;
        }

        IsPreparationStartInProgress = true;
        try
        {
            await StartPreviewCoreAsync();
        }
        finally
        {
            IsPreparationStartInProgress = false;
        }
    }

    public Task FinishAcquisitionAsync() => StopRecordingCoreAsync();

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtime.StateChanged -= OnRuntimeStateChanged;
        runtime.AnalysisFaulted -= OnAnalysisFaulted;
        deviceSession.PropertyChanged -= OnDeviceSessionPropertyChanged;
        channelMapping.DisplayChannelsChanged -= OnDisplayChannelsChanged;
        DisplayPreferences.MessageRaised -= OnDisplayPreferenceMessage;
        DisplayPreferences.Dispose();
        LiveMonitor.Dispose();
        deviceSession.Dispose();
        await runtime.DisposeAsync();
    }

    private Task BrowseSdkLibraryAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择 ANT/eego eego-SDK.dll",
            Filter = "eego-SDK.dll|eego-SDK.dll|DLL 文件|*.dll",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (picker.ShowDialog() == true)
        {
            SdkLibraryPath = picker.FileName;
        }

        return Task.CompletedTask;
    }

    private async Task TestConnectionCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SdkLibraryPath))
        {
            throw new InvalidOperationException("请在放大器设置中选择 eego-SDK.dll 文件。");
        }

        await deviceSession.DiscoverAsync(
            AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(SdkLibraryPath.Trim(), null, null)),
            CancellationToken.None);
        await settingsStore.SaveAsync(new AcquisitionConnectionSettings(
            SdkLibraryPath.Trim(),
            savedReferenceRangeText,
            savedBipolarRangeText,
            RecordingDirectory.Trim(),
            savedSamplingRateHz,
            DisplayPreferences.LiveHighPassHz,
            DisplayPreferences.LiveLowPassHz,
            DisplayPreferences.LiveNotchHz,
            DisplayPreferences.Snapshot.PaperSpeedMillimetersPerSecond,
            DisplayPreferences.Snapshot.HorizontalTimeScaleMode,
            DisplayPreferences.Snapshot.TimebaseSecondsPerScreen), CancellationToken.None);

        AcquisitionStatusText = deviceSession.Snapshot.Detail;
        OperationMessage = Devices.Count > 0
            ? "连接测试成功。采样率和量程均已从当前放大器读取；量程默认采用首个合法兼容组合，需要时可点击“编辑”手动调整。"
            : "未检测到放大器。请检查设备电源、驱动和 USB 连接。";
        if (Devices.Count > 0)
        {
            notifications?.PublishSuccess("放大器连接测试成功，已读取设备能力。");
        }
        else
        {
            notifications?.PublishWarning("未检测到放大器，请检查设备电源、驱动和 USB 连接。");
        }
        RaiseCommandAvailabilityChanged();
    }

    private async Task InitializeDeviceAsync()
    {
        if (!disposed)
        {
            try
            {
                await TestConnectionCoreAsync();
            }
            catch (Exception exception)
            {
                ReportCommandError(exception);
            }
        }
    }

    private async Task StartPreviewCoreAsync()
    {
        var project = SelectedProject ?? throw new InvalidOperationException("请先从项目列表选择项目，再进入采集。");
        var montage = SelectedMontageProfile ?? throw new InvalidOperationException("请先在采集准备页选择导联配置。");
        var channelSnapshot = montage.ChannelConfigurationSnapshot;
        var device = await deviceSession.ConfirmSelectedDeviceAvailabilityAsync(CancellationToken.None);
        MontageValidation.Validate(channelSnapshot, montage.DerivedChannels, montage.AverageReferenceLabels);
        var channelFingerprint = ChannelConfigurationFingerprint.Create(channelSnapshot);
        if (!string.Equals(channelFingerprint, montage.ChannelConfigurationFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
                MontageProfileFingerprint.Create(channelFingerprint, montage.DerivedChannels, montage.AverageReferenceLabels),
                montage.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("所选导联配置的快照指纹无效，请重新保存导联配置。");
        }
        var samplingRateHz = SelectedSamplingRateHz ?? throw new InvalidOperationException("请选择采样率。");
        if (DisplayPreferences.LiveLowPassHz >= samplingRateHz / 2d)
        {
            throw new InvalidOperationException("低通必须低于当前采样率的一半（奈奎斯特频率）。");
        }
        if (DisplayPreferences.LiveNotchHz > 0 && DisplayPreferences.LiveNotchHz >= samplingRateHz / 2d)
        {
            throw new InvalidOperationException("陷波必须低于当前采样率的一半（奈奎斯特频率）。");
        }
        var referenceRange = SelectedReferenceRangeVolts ?? throw new InvalidOperationException("请选择参考量程。");
        var bipolarRange = SelectedBipolarRangeVolts ?? throw new InvalidOperationException("请选择双极量程。");
        AntEegoRangePair.Validate(referenceRange, bipolarRange);
        if (string.IsNullOrWhiteSpace(project.DirectoryPath))
        {
            throw new InvalidOperationException("所选项目没有有效的项目目录，请先打开项目设置修正。");
        }

        // Only mutate session display state after all preparation checks pass.
        channelMapping.ApplyConfiguration(channelSnapshot);
        LiveMonitor.ConfigureMontage(montage);
        runtime.ConfigureDisplayFilter(DisplayPreferences.FilterSettings);
        await deviceSession.ConfigureForStreamAsync(
            AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(
                SdkLibraryPath.Trim(),
                referenceRange,
                bipolarRange,
                ChannelLabelsByNativeIndex: channelMapping.GetCurrentMapping(),
                HardwareReferenceElectrodeLocation: channelSnapshot.HardwareReferenceElectrodeLocation,
                HardwareGroundElectrodeLocation: channelSnapshot.HardwareGroundElectrodeLocation,
                ChannelConfigurationId: channelSnapshot.Id,
                ChannelConfigurationName: channelSnapshot.Name,
                ChannelConfigurationSnapshotJson: JsonSerializer.Serialize(channelSnapshot),
                MontageConfigurationId: montage.Id,
                MontageConfigurationName: montage.Name,
                MontageConfigurationSnapshotJson: JsonSerializer.Serialize(montage))),
            CancellationToken.None);
        savedSamplingRateHz = samplingRateHz;
        savedReferenceRangeText = referenceRange.ToString("R", CultureInfo.InvariantCulture);
        savedBipolarRangeText = bipolarRange.ToString("R", CultureInfo.InvariantCulture);
        await settingsStore.SaveAsync(new AcquisitionConnectionSettings(
            SdkLibraryPath.Trim(),
            savedReferenceRangeText,
            savedBipolarRangeText,
            RecordingDirectory.Trim(),
            savedSamplingRateHz,
            DisplayPreferences.LiveHighPassHz,
            DisplayPreferences.LiveLowPassHz,
            DisplayPreferences.LiveNotchHz,
            DisplayPreferences.Snapshot.PaperSpeedMillimetersPerSecond,
            DisplayPreferences.Snapshot.HorizontalTimeScaleMode,
            DisplayPreferences.Snapshot.TimebaseSecondsPerScreen), CancellationToken.None);
        var projectContext = new AcquisitionProjectContext(
            project.Id,
            project.Number,
            project.Name,
            project.DirectoryPath,
            JsonSerializer.Serialize(project));
        await runtime.StartPreviewAsync(new AcquisitionStreamRequest(device.DeviceId, samplingRateHz, projectContext), CancellationToken.None);
        ApplyStreamMetadata();
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        var notchText = DisplayPreferences.LiveNotchHz > 0 ? $"，{DisplayPreferences.LiveNotchHz:g} Hz 陷波" : "，不使用陷波";
        OperationMessage = $"设备正在实时预览，尚未写入原始记录；显示波形由本地科学引擎执行 {DisplayPreferences.LiveHighPassHz:g}–{DisplayPreferences.LiveLowPassHz:g} Hz 因果滤波{notchText}。";
        notifications?.PublishSuccess(
            $"{FormatLocalClock(runtime.State.ChangedAtUtc)} 实时预览已开始，尚未写入原始记录。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task StartRecordingFromPreviewCoreAsync()
    {
        var project = SelectedProject ?? throw new InvalidOperationException("当前预览没有关联项目。");
        await runtime.StartRecordingAsync(CancellationToken.None);
        LiveRecordingEvents.Clear();
        ApplyStreamMetadata();
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        OperationMessage = "正在记录原始 EEG；记录开始时间、项目、通道和导联快照已写入清单。";
        var recordingStartUtc = runtime.RecordingStartUtc ?? runtime.State.ChangedAtUtc;
        notifications?.PublishSuccess($"{FormatLocalClock(recordingStartUtc)} 已开始记录，数据归属项目“{project.Name}”。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task StopRecordingCoreAsync()
    {
        var stoppedState = runtime.State.State;
        await runtime.StopAsync(CancellationToken.None);
        recordingHistory.Refresh(SelectedProject?.RecordingsDirectory ?? string.Empty);
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        var hadRecording = stoppedState is AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;
        OperationMessage = hadRecording
            ? "记录已完成。原始记录目录包含 manifest.json 与 audit.jsonl。"
            : "实时预览已结束；由于没有开始记录，未创建原始记录文件。";
        var stoppedAt = FormatLocalClock(runtime.State.ChangedAtUtc);
        notifications?.PublishSuccess(hadRecording
            ? $"{stoppedAt} 已停止记录，原始记录与审计信息已保存。"
            : $"{stoppedAt} 实时预览已结束。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task PauseRecordingCoreAsync()
    {
        await runtime.PauseAsync(CancellationToken.None);
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        OperationMessage = "采集已暂停。设备数据流继续排空；暂停期间的样本将作为明确缺口写入审计记录。";
        notifications?.PublishSuccess($"{FormatLocalClock(runtime.State.ChangedAtUtc)} 记录已暂停，设备采集仍在继续。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task ResumeRecordingCoreAsync()
    {
        await runtime.ResumeAsync(CancellationToken.None);
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        OperationMessage = "采集已恢复。暂停期间未保存的样本会作为明确缺口保留在审计记录中。";
        notifications?.PublishSuccess($"{FormatLocalClock(runtime.State.ChangedAtUtc)} 记录已恢复。");
        RaiseCommandAvailabilityChanged();
    }

    private Task RefreshRecordingsAsync()
    {
        recordingHistory.Refresh(SelectedProject?.RecordingsDirectory ?? string.Empty);
        return Task.CompletedTask;
    }

    private async Task SaveChannelMappingAsync()
    {
        await channelMapping.SaveAsync(CancellationToken.None);
        SyncLiveDisplayChannels();
        OperationMessage = channelMapping.StatusText;
        notifications?.PublishSuccess("已保存当前通道标注与显示选择。");
    }

    private static string FormatLocalClock(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    private void ReportCommandError(Exception exception)
    {
        if (exception is AntEegoSdkException sdkException)
        {
            OperationMessage = $"{sdkException.Code}: {sdkException.Message}";
            notifications?.PublishError(OperationMessage);
        }
        else
        {
            OperationMessage = exception.Message;
            notifications?.PublishError(OperationMessage);
        }

        RaiseCommandAvailabilityChanged();
    }

    private void OnRuntimeStateChanged(object? sender, AcquisitionStateSnapshot state)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            AcquisitionStatusText = state.Detail;
            if (state.State is AcquisitionState.Previewing or AcquisitionState.Recording)
            {
                ApplyStreamMetadata();
                _ = RefreshEventDefinitionsAsync();
            }

            LiveMonitor.Refresh();
            RaiseCommandAvailabilityChanged();
        });
    }

    private void OnDeviceSessionPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            if (eventArgs.PropertyName is nameof(DeviceSessionManager.SelectedDevice) or nameof(DeviceSessionManager.Snapshot))
            {
                ApplySelectedDevice(deviceSession.SelectedDevice);
                RaisePropertyChanged(nameof(SelectedDevice));
                RaisePropertyChanged(nameof(DeviceState));
                RaiseDeviceCapabilityPropertiesChanged();
                AcquisitionStatusText = deviceSession.Snapshot.Detail;
            }

            RaiseCommandAvailabilityChanged();
        });
    }

    private void ApplySelectedDevice(AcquisitionDeviceDescriptor? device)
    {
        Replace(SamplingRatesHz, device?.SupportedSamplingRatesHz.Order() ?? Enumerable.Empty<int>());
        Replace(ReferenceRangesVolts, device?.ReferenceRangesVolts?.Order() ?? Enumerable.Empty<double>());
        availableBipolarRanges = device?.BipolarRangesVolts?.Order().ToArray() ?? [];
        selectedSamplingRateHz = savedSamplingRateHz is { } savedSamplingRate &&
            SamplingRatesHz.Contains(savedSamplingRate)
            ? savedSamplingRate
            : SamplingRatesHz.Count == 1 ? SamplingRatesHz[0] : null;
        RestoreOrChooseRangePair();
        automaticReferenceRangeVolts = selectedReferenceRangeVolts;
        automaticBipolarRangeVolts = selectedBipolarRangeVolts;
        isManualInputRangeSelection = false;
        isEditingInputRanges = false;
        RaisePropertyChanged(nameof(SelectedSamplingRateHz));
        RaisePropertyChanged(nameof(SelectedReferenceRangeVolts));
        RaisePropertyChanged(nameof(SelectedBipolarRangeVolts));
        RaiseDeviceCapabilityPropertiesChanged();
        RaiseInputRangePropertiesChanged();
        try
        {
            channelMapping.LoadDevice(device);
            SyncLiveDisplayChannels();
        }
        catch (InvalidDataException exception)
        {
            OperationMessage = exception.Message;
        }
    }

    private void OnDisplayChannelsChanged(object? sender, EventArgs eventArgs)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            SyncLiveDisplayChannels();
            RaiseCommandAvailabilityChanged();
        });
    }

    private Task SaveDisplayPreferencesAsync(LiveDisplayPreferences preferences) =>
        settingsStore.SaveAsync(new AcquisitionConnectionSettings(
            SdkLibraryPath.Trim(),
            savedReferenceRangeText,
            savedBipolarRangeText,
            RecordingDirectory.Trim(),
            savedSamplingRateHz,
            preferences.HighPassHz,
            preferences.LowPassHz,
            preferences.NotchHz,
            preferences.PaperSpeedMillimetersPerSecond,
            preferences.HorizontalTimeScaleMode,
            preferences.TimebaseSecondsPerScreen), CancellationToken.None);

    private void OnDisplayPreferenceMessage(object? sender, string message)
    {
        OperationMessage = message;
        notifications?.PublishSuccess(message);
    }

    private void OnAnalysisFaulted(object? sender, AcquisitionFault fault)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            OperationMessage = $"实时显示滤波不可用（{fault.Code}）：{fault.Detail}。已切换为未滤波原始显示，原始采集仍在继续保存。";
        });
    }

    private void SyncLiveDisplayChannels()
    {
        LiveMonitor.ConfigureChannels(channelMapping.Rows);
    }

    private void ApplyStreamMetadata()
    {
        var metadata = runtime.StreamMetadata;
        if (metadata is null)
        {
            return;
        }

        Replace(Channels, metadata.Channels.Select(channel => new AcquisitionChannelRow(
            channel.StreamIndex,
            channel.NativeChannelIndex,
            channel.Kind.ToString(),
            channel.Label ?? "SDK 未提供",
            channel.Unit)));
    }

    private void InvalidateConnectionTest()
    {
        SamplingRatesHz.Clear();
        ReferenceRangesVolts.Clear();
        BipolarRangesVolts.Clear();
        availableBipolarRanges = [];
        automaticReferenceRangeVolts = null;
        automaticBipolarRangeVolts = null;
        isManualInputRangeSelection = false;
        isEditingInputRanges = false;
        Channels.Clear();
        deviceSession.Invalidate("SDK 文件已修改，请重新测试放大器连接。");
        OperationMessage = "SDK 文件已修改，请重新测试放大器连接。";
        RaiseCommandAvailabilityChanged();
        RaiseDeviceCapabilityPropertiesChanged();
        RaiseInputRangePropertiesChanged();
    }

    private AcquisitionConnectionSettings LoadSettings()
    {
        try
        {
            return settingsStore.Load();
        }
        catch (InvalidDataException)
        {
            operationMessage = "本机采集设置文件无效；请重新选择 SDK 文件。";
            return AcquisitionConnectionSettings.CreateDefault();
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static double? RestoreRange(string savedValue, IReadOnlyCollection<double> supportedValues)
    {
        return double.TryParse(savedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            supportedValues.Contains(parsed)
            ? parsed
            : null;
    }

    private void RefreshBipolarRangeChoices()
    {
        var compatibleRanges = selectedReferenceRangeVolts is { } referenceRange
            ? AntEegoRangePair.GetCompatibleBipolarRanges(referenceRange, availableBipolarRanges)
            : [];
        Replace(BipolarRangesVolts, compatibleRanges);

        var restored = RestoreRange(savedBipolarRangeText, BipolarRangesVolts);
        selectedBipolarRangeVolts = BipolarRangesVolts.Contains(selectedBipolarRangeVolts ?? double.NaN)
            ? selectedBipolarRangeVolts
            : restored ?? (BipolarRangesVolts.Count == 1 ? BipolarRangesVolts[0] : null);
        RaisePropertyChanged(nameof(SelectedBipolarRangeVolts));
    }

    private void RestoreOrChooseRangePair()
    {
        var savedReference = RestoreRange(savedReferenceRangeText, ReferenceRangesVolts);
        var savedBipolar = RestoreRange(savedBipolarRangeText, availableBipolarRanges);
        var pair = savedReference is { } referenceRange && savedBipolar is { } bipolarRange &&
            AntEegoRangePair.IsCompatible(referenceRange, bipolarRange)
            ? (ReferenceRangeVolts: referenceRange, BipolarRangeVolts: bipolarRange)
            : AntEegoRangePair.FindFirstCompatiblePair(ReferenceRangesVolts, availableBipolarRanges);

        selectedReferenceRangeVolts = pair?.ReferenceRangeVolts;
        Replace(
            BipolarRangesVolts,
            pair is null
                ? []
                : AntEegoRangePair.GetCompatibleBipolarRanges(pair.Value.ReferenceRangeVolts, availableBipolarRanges));
        selectedBipolarRangeVolts = pair?.BipolarRangeVolts;
    }

    private void RaiseCommandAvailabilityChanged()
    {
        RaisePropertyChanged(nameof(CanTestConnection));
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanStartRecording));
        RaisePropertyChanged(nameof(CanStop));
        RaisePropertyChanged(nameof(CanPause));
        RaisePropertyChanged(nameof(CanResume));
        RaisePropertyChanged(nameof(CanEditInputRanges));
        RaisePropertyChanged(nameof(CanConfirmInputRangeEdit));
        DisplayPreferences.RefreshAvailability();
    }

    private void RaiseProjectSummaryPropertiesChanged()
    {
        RaisePropertyChanged(nameof(ProjectNameText));
        RaisePropertyChanged(nameof(ProjectNumberText));
        RaisePropertyChanged(nameof(ProjectDirectoryText));
        RaisePropertyChanged(nameof(HasSelectedProject));
    }

    private int CountInputChannels(AcquisitionChannelKind kind) =>
        SelectedDevice?.ChannelCapabilities?.Count(capability => capability.Kind == kind) ?? 0;

    private static string DescribeInputKind(AcquisitionChannelKind kind) => kind switch
    {
        AcquisitionChannelKind.Trigger => "Trigger",
        AcquisitionChannelKind.ImpedanceReference => "阻抗参考",
        AcquisitionChannelKind.ImpedanceGround => "阻抗接地",
        AcquisitionChannelKind.SampleCounter => "采样计数",
        AcquisitionChannelKind.Accelerometer => "加速度",
        AcquisitionChannelKind.Gyroscope => "陀螺仪",
        AcquisitionChannelKind.Magnetometer => "磁力计",
        _ => kind.ToString(),
    };

    private static string FormatInputRange(double? volts)
    {
        if (volts is not { } value || value <= 0) return "未读取";
        var microvolts = value * 1_000_000d;
        return microvolts < 1_000_000d
            ? $"±{microvolts:g} µV"
            : $"±{value:g} V";
    }

    private void RaiseInputRangePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsEditingInputRanges));
        RaisePropertyChanged(nameof(InputRangeSelectionModeText));
        RaisePropertyChanged(nameof(ReferenceInputRangeText));
        RaisePropertyChanged(nameof(BipolarInputRangeText));
        RaisePropertyChanged(nameof(CanEditInputRanges));
        RaisePropertyChanged(nameof(CanConfirmInputRangeEdit));
    }

    private void RaiseDeviceCapabilityPropertiesChanged()
    {
        RaisePropertyChanged(nameof(DeviceConnectionStatusText));
        RaisePropertyChanged(nameof(IsDeviceConnected));
        RaisePropertyChanged(nameof(DeviceVendorText));
        RaisePropertyChanged(nameof(DeviceModelText));
        RaisePropertyChanged(nameof(DeviceSerialNumberText));
        RaisePropertyChanged(nameof(DeviceInputCapabilitySummary));
        RaisePropertyChanged(nameof(SupportedSamplingRatesText));
        RaisePropertyChanged(nameof(ReferenceInputCount));
        RaisePropertyChanged(nameof(BipolarInputCount));
        RaisePropertyChanged(nameof(OtherInputCapabilitiesText));
        RaisePropertyChanged(nameof(CanEditInputRanges));
    }

    private static string DescribeDeviceVendor(string? driverId) => driverId?.Trim().ToLowerInvariant() switch
    {
        "ant-eego" => "ANT",
        null or "" => "SDK 未报告厂商",
        _ => driverId.Trim(),
    };
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using BrainPlatform.Desktop.Modules.Acquisition.Services;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed record AcquisitionChannelRow(int StreamIndex, int NativeChannelIndex, string Role, string Label, string Unit);

public sealed partial class AcquisitionWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly DeviceSessionManager deviceSession;
    private readonly Dispatcher dispatcher;
    private readonly RecordingHistoryViewModel recordingHistory;
    private readonly ChannelMappingViewModel channelMapping;
    private readonly OperationNotificationCenter? notifications;
    private readonly EventDefinitionService eventDefinitionService;
    private readonly AcquisitionSessionWorkflow workflow;
    private readonly AcquisitionDeviceSetupController setup;
    private readonly AcquisitionRecordingEventController recordingEvents;
    private string sdkLibraryPath;
    private string recordingDirectory;
    private MontageProfile? selectedMontageProfile;
    private MontageProfile? liveDisplayMontageProfile;
    private ResearchProject? selectedProject;
    private string acquisitionStatusText = "尚未连接放大器。";
    private string operationMessage = "打开“放大器设置”，选择 SDK 文件后测试连接。";
    private bool isPreparationStartInProgress;
    private bool disposed;

    public AcquisitionWorkspaceViewModel(
        IAcquisitionSettingsStore? settingsStore = null,
        ConfiguredAcquisitionRuntime? runtime = null,
        DeviceSessionManager? deviceSession = null,
        Dispatcher? dispatcher = null,
        OperationNotificationCenter? notifications = null,
        EventDefinitionService? eventDefinitionService = null,
        IRecordingRegistrationClient? recordingRegistrationClient = null,
        MontageProfileStore? montageStore = null)
    {
        var resolvedSettingsStore = settingsStore ?? new LocalAcquisitionSettingsStore();
        this.deviceSession = deviceSession ?? new DeviceSessionManager(runtime ?? new ConfiguredAcquisitionRuntime());
        this.runtime = this.deviceSession.Runtime;
        if (runtime is not null && !ReferenceEquals(runtime, this.runtime))
        {
            throw new ArgumentException("设备会话必须使用同一个采集运行时。", nameof(deviceSession));
        }
        this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        this.notifications = notifications;
        this.eventDefinitionService = eventDefinitionService ?? new EventDefinitionService(new EventDefinitionStore());
        workflow = new AcquisitionSessionWorkflow(
            this.deviceSession, this.runtime, resolvedSettingsStore,
            montageStore ?? new MontageProfileStore(), recordingRegistrationClient);
        var settings = workflow.LoadSettings(out var loadWarning);
        if (loadWarning is not null) operationMessage = loadWarning;
        sdkLibraryPath = AntEegoSdkPathResolver.Resolve(settings.SdkLibraryPath, AppContext.BaseDirectory);
        recordingDirectory = settings.RecordingDirectory;
        setup = new AcquisitionDeviceSetupController(
            this.deviceSession, this.runtime, settings.SamplingRateHz, settings.ReferenceRangeText, settings.BipolarRangeText);
        recordingEvents = new AcquisitionRecordingEventController(this.runtime, this.eventDefinitionService);
        recordingHistory = new RecordingHistoryViewModel(new LocalRecordingCatalog());
        recordingHistory.Refresh(recordingDirectory);
        channelMapping = new ChannelMappingViewModel(notifications: notifications);
        LiveMonitor = new LiveMonitoringViewModel(
            () => this.runtime.State, () => this.runtime.StreamMetadata, () => this.runtime.GetDisplaySnapshot(),
            this.dispatcher, () => this.runtime.GetDisplayFilterBoundaries(), () => this.runtime.RecordingFirstSampleCounter,
            () => LiveRecordingEvents.ToArray(), () => this.runtime.RecordingStartUtc, () => this.runtime.GetLifecycleBoundaries());
        DisplayPreferences = new LiveDisplayPreferencesViewModel(
            this.runtime, LiveMonitor,
            new LiveDisplayPreferences(
                settings.LiveHighPassHz, settings.LiveLowPassHz, settings.LiveNotchHz,
                settings.PaperSpeedMillimetersPerSecond, settings.HorizontalTimeScaleMode, settings.TimebaseSecondsPerScreen),
            SaveDisplayPreferencesAsync, () => IsPreparationStartInProgress);
        DisplayPreferences.MessageRaised += OnDisplayPreferenceMessage;
        channelMapping.DisplayChannelsChanged += OnDisplayChannelsChanged;
        this.deviceSession.PropertyChanged += OnDeviceSessionPropertyChanged;
        this.runtime.StateChanged += OnRuntimeStateChanged;
        this.runtime.AnalysisFaulted += OnAnalysisFaulted;
        setup.PropertyChanged += OnSetupPropertyChanged;
        recordingEvents.PropertyChanged += OnRecordingEventsPropertyChanged;
        recordingEvents.FeedbackRaised += OnRecordingEventFeedbackRaised;
        LiveAnalysis = new LiveAnalysisWorkspaceViewModel(this.runtime, this.dispatcher, ReportCommandError);
        StorageStatus = new AcquisitionStorageStatusViewModel(
            () => this.runtime.State.State, () => this.runtime.RecordingDirectory, this.dispatcher);

        BrowseSdkLibraryCommand = new AsyncRelayCommand(BrowseSdkLibraryAsync, ReportCommandError);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionCoreAsync, ReportCommandError);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingFromPreviewCoreAsync, ReportCommandError);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingCoreAsync, ReportCommandError);
        PauseRecordingCommand = new AsyncRelayCommand(PauseRecordingCoreAsync, ReportCommandError);
        ResumeRecordingCommand = new AsyncRelayCommand(ResumeRecordingCoreAsync, ReportCommandError);
        RefreshRecordingsCommand = new AsyncRelayCommand(RefreshRecordingsAsync, ReportCommandError);
        SaveChannelMappingCommand = new AsyncRelayCommand(SaveChannelMappingAsync, ReportCommandError);
        BeginInputRangeEditCommand = new AsyncRelayCommand(() => { setup.BeginInputRangeEdit(); return Task.CompletedTask; }, ReportCommandError);
        CancelInputRangeEditCommand = new AsyncRelayCommand(() => { setup.CancelInputRangeEdit(); return Task.CompletedTask; }, ReportCommandError);
        ConfirmInputRangeEditCommand = new AsyncRelayCommand(() => { setup.ConfirmInputRangeEdit(); return Task.CompletedTask; }, ReportCommandError);
        MarkSelectedEventCommand = new AsyncRelayCommand(MarkSelectedEventAsync, ReportCommandError);
        if (!string.IsNullOrWhiteSpace(sdkLibraryPath)) _ = InitializeDeviceAsync();
    }

    public ReadOnlyObservableCollection<AcquisitionDeviceDescriptor> Devices => deviceSession.Devices;
    public ObservableCollection<int> SamplingRatesHz => setup.SamplingRatesHz;
    public ObservableCollection<double> ReferenceRangesVolts => setup.ReferenceRangesVolts;
    public ObservableCollection<double> BipolarRangesVolts => setup.BipolarRangesVolts;
    public ObservableCollection<AcquisitionChannelRow> Channels { get; } = [];
    public RecordingHistoryViewModel RecordingHistory => recordingHistory;
    public ChannelMappingViewModel ChannelMapping => channelMapping;
    public DeviceSessionManager DeviceSession => deviceSession;
    public LiveMonitoringViewModel LiveMonitor { get; }
    public LiveAnalysisWorkspaceViewModel LiveAnalysis { get; }
    public AcquisitionStorageStatusViewModel StorageStatus { get; }
    public LiveDisplayPreferencesViewModel DisplayPreferences { get; }
    public EventDefinitionService EventDefinitionService => eventDefinitionService;
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
            if (SetProperty(ref sdkLibraryPath, value)) InvalidateConnectionTest();
        }
    }

    public string RecordingDirectory { get => recordingDirectory; set => SetProperty(ref recordingDirectory, value); }
    public RegisteredRecording? LastRegisteredRecording { get; private set; }
    public AcquisitionDeviceDescriptor? SelectedDevice { get => deviceSession.SelectedDevice; set => deviceSession.SelectDevice(value); }

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

    /// <summary>
    /// Transient montage used only to project the live waveform. The selected
    /// acquisition montage remains the immutable recording snapshot.
    /// </summary>
    public MontageProfile? LiveDisplayMontageProfile
    {
        get => liveDisplayMontageProfile;
        set
        {
            if (value is null || ReferenceEquals(value, liveDisplayMontageProfile)) return;
            if (TrySelectTemporaryDisplayMontage(value)) return;
            if (!CanSwitchLiveDisplayMontage) { RaisePropertyChanged(nameof(LiveDisplayMontageProfile)); return; }
            if (!IsCompatibleLiveDisplayMontage(value))
            {
                OperationMessage = "实时显示导联必须使用当前采集通道配置。";
                RaisePropertyChanged(nameof(LiveDisplayMontageProfile));
                return;
            }
            if (SetProperty(ref liveDisplayMontageProfile, value))
            {
                if (temporaryWaveformMontage is not null) regularDisplayMontage = value;
                runtime.SelectLiveMontage(value);
                LiveMonitor.ConfigureMontage(value);
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

    public int? SelectedSamplingRateHz { get => setup.SelectedSamplingRateHz; set => setup.SelectedSamplingRateHz = value; }
    public double? SelectedReferenceRangeVolts { get => setup.SelectedReferenceRangeVolts; set => setup.SelectedReferenceRangeVolts = value; }
    public double? SelectedBipolarRangeVolts { get => setup.SelectedBipolarRangeVolts; set => setup.SelectedBipolarRangeVolts = value; }
    public DeviceReadiness DeviceState => deviceSession.Readiness;
    public string ProjectNameText => SelectedProject?.Name ?? "未选择项目";
    public string ProjectNumberText => SelectedProject?.Number ?? "未生成";
    public string ProjectDirectoryText => SelectedProject?.DirectoryPath ?? "未配置目录";
    public bool HasSelectedProject => SelectedProject is not null;
    public string DeviceConnectionStatusText => setup.DeviceConnectionStatusText;
    public bool IsDeviceConnected => setup.IsDeviceConnected;
    public string DeviceVendorText => setup.DeviceVendorText;
    public string DeviceModelText => setup.DeviceModelText;
    public string DeviceSerialNumberText => setup.DeviceSerialNumberText;
    public string DeviceInputCapabilitySummary => setup.DeviceInputCapabilitySummary;
    public bool IsEditingInputRanges => setup.IsEditingInputRanges;
    public bool CanEditInputRanges => setup.CanEditInputRanges;
    public bool CanConfirmInputRangeEdit => setup.CanConfirmInputRangeEdit;
    public string InputRangeSelectionModeText => setup.InputRangeSelectionModeText;
    public string ReferenceInputRangeText => setup.ReferenceInputRangeText;
    public string BipolarInputRangeText => setup.BipolarInputRangeText;
    public string SupportedSamplingRatesText => setup.SupportedSamplingRatesText;
    public int ReferenceInputCount => setup.ReferenceInputCount;
    public int BipolarInputCount => setup.BipolarInputCount;
    public string OtherInputCapabilitiesText => setup.OtherInputCapabilitiesText;
    public string SelectedMontageChannelConfigurationName => SelectedMontageProfile?.ChannelConfigurationName ?? "未选择";
    public int SelectedMontageOutputChannelCount => SelectedMontageProfile?.ChannelCount ?? 0;
    public string AcquisitionStatusText { get => acquisitionStatusText; private set { if (SetProperty(ref acquisitionStatusText, value)) RaiseCommandAvailabilityChanged(); } }
    public string OperationMessage { get => operationMessage; private set => SetProperty(ref operationMessage, value); }
    public bool CanTestConnection => !runtime.IsDeviceOccupied;
    public bool CanStart => setup.HasValidStartSetup && SelectedMontageProfile is not null && SelectedProject is not null && !IsPreparationStartInProgress;
    public bool IsPreparationStartInProgress { get => isPreparationStartInProgress; private set { if (SetProperty(ref isPreparationStartInProgress, value)) RaiseCommandAvailabilityChanged(); } }
    public bool CanStartRecording => runtime.State.State == AcquisitionState.Previewing;
    public bool CanSwitchLiveDisplayMontage => runtime.State.State == AcquisitionState.Previewing;
    public bool CanStop => runtime.IsDeviceOccupied || openingImpedance;
    public bool CanPause => runtime.State.State == AcquisitionState.Recording;
    public bool CanResume => runtime.State.State == AcquisitionState.Paused;

    public async Task StartPreparedPreviewAsync()
    {
        if (IsPreparationStartInProgress) return;
        IsPreparationStartInProgress = true;
        try
        {
            await DisplayPreferences.CompletePendingFilterUpdatesAsync();
            var resumeFrozenDisplay = LiveMonitor.IsDisplayFrozen;
            await StartPreviewCoreAsync();
            if (resumeFrozenDisplay) LiveMonitor.ResumePreviewDisplay();
            else LiveMonitor.ClearPreviewDisplayContinuity();
        }
        finally { IsPreparationStartInProgress = false; }
    }

    public Task FinishAcquisitionAsync() => StopRecordingCoreAsync();

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await CloseImpedanceWindowAsync();
        disposed = true;
        runtime.StateChanged -= OnRuntimeStateChanged;
        runtime.AnalysisFaulted -= OnAnalysisFaulted;
        deviceSession.PropertyChanged -= OnDeviceSessionPropertyChanged;
        channelMapping.DisplayChannelsChanged -= OnDisplayChannelsChanged;
        DisplayPreferences.MessageRaised -= OnDisplayPreferenceMessage;
        setup.PropertyChanged -= OnSetupPropertyChanged;
        recordingEvents.PropertyChanged -= OnRecordingEventsPropertyChanged;
        recordingEvents.FeedbackRaised -= OnRecordingEventFeedbackRaised;
        ClearTemporaryWaveformMontage();
        recordingEvents.Dispose();
        LiveAnalysis.Dispose();
        StorageStatus.Dispose();
        DisplayPreferences.Dispose();
        LiveMonitor.Dispose();
        deviceSession.Dispose();
        await runtime.DisposeAsync();
    }

    private Task BrowseSdkLibraryAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择 POP eego-SDK.dll", Filter = "eego-SDK.dll|eego-SDK.dll|DLL 文件|*.dll",
            CheckFileExists = true, Multiselect = false,
        };
        if (picker.ShowDialog() == true) SdkLibraryPath = picker.FileName;
        return Task.CompletedTask;
    }

    private async Task TestConnectionCoreAsync()
    {
        await workflow.TestConnectionAsync(
            SdkLibraryPath, RecordingDirectory, setup.SavedSamplingRateHz, setup.SavedReferenceRangeText,
            setup.SavedBipolarRangeText, DisplayPreferences.Snapshot, CancellationToken.None);
        AcquisitionStatusText = deviceSession.Snapshot.Detail;
        OperationMessage = Devices.Count > 0
            ? "连接测试成功。采样率和量程均已从当前放大器读取；量程默认采用首个合法兼容组合，需要时可点击“编辑”手动调整。"
            : "未检测到放大器。请检查设备电源、驱动和 USB 连接。";
        if (Devices.Count > 0) notifications?.PublishSuccess("放大器连接测试成功，已读取设备能力。");
        else notifications?.PublishWarning("未检测到放大器，请检查设备电源、驱动和 USB 连接。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task InitializeDeviceAsync()
    {
        if (disposed) return;
        try { await TestConnectionCoreAsync(); }
        catch (Exception exception) { ReportCommandError(exception); }
    }

    private AcquisitionPreviewStartInput BuildPreviewStartInput() => new(
        SelectedProject, SelectedMontageProfile, SelectedSamplingRateHz, SelectedReferenceRangeVolts,
        SelectedBipolarRangeVolts, DisplayPreferences.Snapshot, SdkLibraryPath, RecordingDirectory,
        SubjectCode, SubjectName, SubjectId, SubjectSex, AgeAtRecordingYears);

    private async Task StartPreviewCoreAsync()
    {
        var prepared = await workflow.ValidateAndPreparePreviewAsync(BuildPreviewStartInput());
        void EnsureInputUnchanged()
        {
            if (AcquisitionSessionWorkflow.HasInputChanged(prepared.InputSnapshot, BuildPreviewStartInput()))
                throw new InvalidOperationException("启动准备期间采集或滤波参数已修改，请重新开始预览。");
        }
        EnsureInputUnchanged();

        // Only mutate session display state after all preparation checks pass.
        channelMapping.ApplyConfiguration(prepared.ChannelSnapshot);
        liveDisplayMontageProfile = prepared.Montage;
        RaisePropertyChanged(nameof(LiveDisplayMontageProfile));
        LiveMonitor.ConfigureMontage(prepared.Montage);
        ClearTemporaryWaveformMontage();
        runtime.ConfigureDisplayFilter(prepared.FilterSettings);

        await workflow.StartPreparedStreamAsync(
            prepared, channelMapping.GetCurrentMapping(), setup.RecordPersistedSelection, EnsureInputUnchanged);
        ApplyStreamMetadata();
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        var notchText = prepared.DisplayPreferences.NotchHz > 0 ? $"，{prepared.DisplayPreferences.NotchHz:g} Hz 陷波" : "，不使用陷波";
        OperationMessage = $"设备正在实时预览，尚未写入原始记录；显示波形由本地科学引擎执行 {prepared.DisplayPreferences.HighPassHz:g}–{prepared.DisplayPreferences.LowPassHz:g} Hz 因果滤波{notchText}。";
        notifications?.PublishSuccess($"{FormatLocalClock(runtime.State.ChangedAtUtc)} 实时预览已开始，尚未写入原始记录。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task StartRecordingFromPreviewCoreAsync()
    {
        var project = await workflow.StartRecordingAsync(SelectedProject);
        recordingEvents.ClearLiveEvents();
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
        var outcome = await workflow.StopAndRegisterAsync(CloseImpedanceWindowAsync);
        ClearTemporaryWaveformMontage();
        recordingHistory.Refresh(SelectedProject?.RecordingsDirectory ?? string.Empty);
        LiveMonitor.Refresh();
        AcquisitionStatusText = outcome.StatusDetail;
        if (outcome.NewlyRegisteredRecording is not null)
        {
            LastRegisteredRecording = outcome.NewlyRegisteredRecording;
            RaisePropertyChanged(nameof(LastRegisteredRecording));
        }
        if (outcome.RegistrationWarning is not null) notifications?.PublishWarning(outcome.RegistrationWarning);
        OperationMessage = outcome.HadRecording
            ? LastRegisteredRecording is null
                ? "记录已完成。原始记录已保存，尚未注册到分析服务。"
                : $"记录已完成，已注册分析记录 {LastRegisteredRecording.Id}。"
            : "实时预览已结束；由于没有开始记录，未创建原始记录文件。";
        var stoppedAt = FormatLocalClock(outcome.ChangedAtUtc);
        notifications?.PublishSuccess(outcome.HadRecording
            ? $"{stoppedAt} 已停止记录，原始记录与审计信息已保存。"
            : $"{stoppedAt} 实时预览已结束。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task PauseRecordingCoreAsync()
    {
        await workflow.PauseAsync();
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        OperationMessage = "采集已暂停。设备数据流继续排空；暂停期间的样本将作为明确缺口写入审计记录。";
        notifications?.PublishSuccess($"{FormatLocalClock(runtime.State.ChangedAtUtc)} 记录已暂停，设备采集仍在继续。");
        RaiseCommandAvailabilityChanged();
    }

    private async Task ResumeRecordingCoreAsync()
    {
        await workflow.ResumeAsync();
        LiveMonitor.Refresh();
        AcquisitionStatusText = runtime.State.Detail;
        OperationMessage = "采集已恢复。暂停期间未保存的样本会作为明确缺口保留在审计记录中。";
        notifications?.PublishSuccess($"{FormatLocalClock(runtime.State.ChangedAtUtc)} 记录已恢复。");
        RaiseCommandAvailabilityChanged();
    }

    private Task RefreshRecordingsAsync() { recordingHistory.Refresh(SelectedProject?.RecordingsDirectory ?? string.Empty); return Task.CompletedTask; }

    private async Task SaveChannelMappingAsync()
    {
        await channelMapping.SaveAsync(CancellationToken.None);
        SyncLiveDisplayChannels();
        OperationMessage = channelMapping.StatusText;
        notifications?.PublishSuccess("已保存当前通道标注与显示选择。");
    }

    private static string FormatLocalClock(DateTimeOffset utc) => utc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);

    private void ReportCommandError(Exception exception)
    {
        OperationMessage = exception is AntEegoSdkException sdkException
            ? $"{sdkException.Code}: {sdkException.Message}"
            : exception.Message;
        notifications?.PublishError(OperationMessage);
        RaiseCommandAvailabilityChanged();
    }

    private void OnRuntimeStateChanged(object? sender, AcquisitionStateSnapshot state) => _ = dispatcher.InvokeAsync(() =>
    {
        if (disposed) return;
        AcquisitionStatusText = state.Detail;
        LiveAnalysis.RefreshSession();
        StorageStatus.RefreshState();
        if (state.State is AcquisitionState.Previewing or AcquisitionState.Recording) { ApplyStreamMetadata(); _ = RefreshEventDefinitionsAsync(); }
        if (state.State is AcquisitionState.Stopped or AcquisitionState.Faulted or AcquisitionState.Ready) { ClearTemporaryWaveformMontage(); CancelActiveInterval(); }
        LiveMonitor.Refresh();
        RaisePropertyChanged(nameof(CanSelectWaveformMontage));
        RaiseCommandAvailabilityChanged();
    });

    private void OnDeviceSessionPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs) => _ = dispatcher.InvokeAsync(() =>
    {
        if (disposed) return;
        if (eventArgs.PropertyName is nameof(DeviceSessionManager.SelectedDevice) or nameof(DeviceSessionManager.Snapshot))
        {
            ApplySelectedDevice(deviceSession.SelectedDevice);
            RaisePropertyChanged(nameof(SelectedDevice));
            RaisePropertyChanged(nameof(DeviceState));
            setup.RaiseCapabilityPropertiesChanged();
            AcquisitionStatusText = deviceSession.Snapshot.Detail;
        }
        RaiseCommandAvailabilityChanged();
    });

    private void ApplySelectedDevice(AcquisitionDeviceDescriptor? device)
    {
        if (!setup.PrepareDeviceSetup(device)) return;
        try
        {
            channelMapping.LoadDevice(device);
            SyncLiveDisplayChannels();
            setup.MarkDeviceApplied(device);
        }
        catch (InvalidDataException exception) { OperationMessage = exception.Message; }
    }

    private void OnDisplayChannelsChanged(object? sender, EventArgs eventArgs) => _ = dispatcher.InvokeAsync(() =>
    {
        if (disposed) return;
        SyncLiveDisplayChannels();
        RaiseCommandAvailabilityChanged();
    });

    private void OnSetupPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName)) RaisePropertyChanged(eventArgs.PropertyName);
        if (eventArgs.PropertyName is nameof(AcquisitionDeviceSetupController.SelectedSamplingRateHz)
            or nameof(AcquisitionDeviceSetupController.SelectedReferenceRangeVolts)
            or nameof(AcquisitionDeviceSetupController.SelectedBipolarRangeVolts))
        {
            RaisePropertyChanged(nameof(CanStart));
        }
    }

    private void OnRecordingEventsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName)) RaisePropertyChanged(eventArgs.PropertyName);
    }

    private void OnRecordingEventFeedbackRaised(object? sender, AcquisitionEventFeedback feedback)
    {
        OperationMessage = feedback.Message;
        if (feedback.IsSuccess) notifications?.PublishSuccess(feedback.Message);
        else notifications?.PublishInformation(feedback.Message);
    }

    private Task SaveDisplayPreferencesAsync(LiveDisplayPreferences preferences) => workflow.SaveSettingsAsync(
        SdkLibraryPath, RecordingDirectory, setup.SavedSamplingRateHz, setup.SavedReferenceRangeText, setup.SavedBipolarRangeText, preferences);

    private void OnDisplayPreferenceMessage(object? sender, string message) { OperationMessage = message; notifications?.PublishSuccess(message); }

    private void OnAnalysisFaulted(object? sender, AcquisitionFault fault) => _ = dispatcher.InvokeAsync(() =>
    {
        if (disposed) return;
        OperationMessage = $"实时显示滤波不可用（{fault.Code}）：{fault.Detail}。已切换为未滤波原始显示，原始采集仍在继续保存。";
    });

    private void SyncLiveDisplayChannels() => LiveMonitor.ConfigureChannels(channelMapping.Rows);

    private void ApplyStreamMetadata()
    {
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(ApplyStreamMetadata);
            return;
        }
        var metadata = runtime.StreamMetadata;
        if (metadata is null || disposed) return;
        Channels.Clear();
        foreach (var ch in metadata.Channels)
            Channels.Add(new AcquisitionChannelRow(ch.StreamIndex, ch.NativeChannelIndex, ch.Kind.ToString(), ch.Label ?? "SDK 未提供", ch.Unit));
    }

    private void InvalidateConnectionTest()
    {
        setup.InvalidateSelection();
        Channels.Clear();
        deviceSession.Invalidate("SDK 文件已修改，请重新测试放大器连接。");
        OperationMessage = "SDK 文件已修改，请重新测试放大器连接。";
        RaiseCommandAvailabilityChanged();
    }

    private void RaiseCommandAvailabilityChanged()
    {
        RaisePropertyChanged(nameof(CanTestConnection));
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanStartRecording));
        RaisePropertyChanged(nameof(CanStartImpedance));
        RaisePropertyChanged(nameof(CanSwitchLiveDisplayMontage));
        RaisePropertyChanged(nameof(CanStop));
        RaisePropertyChanged(nameof(CanPause));
        RaisePropertyChanged(nameof(CanResume));
        RaisePropertyChanged(nameof(CanEditInputRanges));
        RaisePropertyChanged(nameof(CanConfirmInputRangeEdit));
        DisplayPreferences.RefreshAvailability();
    }

    private bool IsCompatibleLiveDisplayMontage(MontageProfile profile) =>
        SelectedMontageProfile is { } selected &&
        string.Equals(profile.ChannelConfigurationFingerprint, selected.ChannelConfigurationFingerprint, StringComparison.Ordinal);

    private void RaiseProjectSummaryPropertiesChanged()
    {
        RaisePropertyChanged(nameof(ProjectNameText));
        RaisePropertyChanged(nameof(ProjectNumberText));
        RaisePropertyChanged(nameof(ProjectDirectoryText));
        RaisePropertyChanged(nameof(HasSelectedProject));
    }
}

using System.IO;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.Services;

internal sealed record AcquisitionPreviewStartInput(
    ResearchProject? Project,
    MontageProfile? Montage,
    int? SamplingRateHz,
    double? ReferenceRangeVolts,
    double? BipolarRangeVolts,
    LiveDisplayPreferences DisplayPreferences,
    string SdkLibraryPath,
    string RecordingDirectory,
    string SubjectCode,
    string SubjectName,
    string? SubjectId,
    string? SubjectSex,
    int? AgeAtRecordingYears);

internal sealed record PreparedPreviewStart(
    AcquisitionPreviewStartInput InputSnapshot,
    ResearchProject Project,
    MontageProfile Montage,
    ChannelConfigurationProfile ChannelSnapshot,
    AcquisitionDeviceDescriptor Device,
    int SamplingRateHz,
    double ReferenceRangeVolts,
    double BipolarRangeVolts,
    LiveDisplayPreferences DisplayPreferences,
    LiveDisplayFilterSettings FilterSettings,
    string SdkLibraryPath,
    string RecordingDirectory,
    MontageProfile[] HistoricalProfiles,
    AcquisitionProjectContext ProjectContext);

internal sealed record StoppedAcquisitionOutcome(
    bool HadRecording,
    RegisteredRecording? NewlyRegisteredRecording,
    string? RegistrationWarning,
    string StatusDetail,
    DateTimeOffset ChangedAtUtc);

/// <summary>
/// Stateless workflow collaborator coordinating settings persistence, SDK discovery,
/// strictly ordered pre-start validation &amp; preview launch, recording transitions,
/// and stop + non-fatal backend WPF recording registration.
/// </summary>
internal sealed class AcquisitionSessionWorkflow
{
    private readonly DeviceSessionManager deviceSession;
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly IAcquisitionSettingsStore settingsStore;
    private readonly MontageProfileStore montageStore;
    private readonly IRecordingRegistrationClient? recordingRegistrationClient;

    public AcquisitionSessionWorkflow(
        DeviceSessionManager deviceSession,
        ConfiguredAcquisitionRuntime runtime,
        IAcquisitionSettingsStore settingsStore,
        MontageProfileStore montageStore,
        IRecordingRegistrationClient? recordingRegistrationClient)
    {
        this.deviceSession = deviceSession;
        this.runtime = runtime;
        this.settingsStore = settingsStore;
        this.montageStore = montageStore;
        this.recordingRegistrationClient = recordingRegistrationClient;
    }

    public AcquisitionConnectionSettings LoadSettings(out string? warningMessage)
    {
        try
        {
            warningMessage = null;
            return settingsStore.Load();
        }
        catch (InvalidDataException)
        {
            warningMessage = "本机采集设置文件无效；请重新选择 SDK 文件。";
            return AcquisitionConnectionSettings.CreateDefault();
        }
    }

    public Task SaveSettingsAsync(
        string sdkLibraryPath,
        string recordingDirectory,
        int? savedSamplingRateHz,
        string savedReferenceRangeText,
        string savedBipolarRangeText,
        LiveDisplayPreferences preferences,
        CancellationToken cancellationToken = default) =>
        settingsStore.SaveAsync(new AcquisitionConnectionSettings(
            sdkLibraryPath.Trim(),
            savedReferenceRangeText,
            savedBipolarRangeText,
            recordingDirectory.Trim(),
            savedSamplingRateHz,
            preferences.HighPassHz,
            preferences.LowPassHz,
            preferences.NotchHz,
            preferences.PaperSpeedMillimetersPerSecond,
            preferences.HorizontalTimeScaleMode,
            preferences.TimebaseSecondsPerScreen), cancellationToken);

    public async Task TestConnectionAsync(
        string sdkLibraryPath,
        string recordingDirectory,
        int? savedSamplingRateHz,
        string savedReferenceRangeText,
        string savedBipolarRangeText,
        LiveDisplayPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sdkLibraryPath))
        {
            throw new InvalidOperationException("请在放大器设置中选择 eego-SDK.dll 文件。");
        }

        await deviceSession.DiscoverAsync(
            AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(sdkLibraryPath.Trim(), null, null)),
            cancellationToken);
        await SaveSettingsAsync(
            sdkLibraryPath,
            recordingDirectory,
            savedSamplingRateHz,
            savedReferenceRangeText,
            savedBipolarRangeText,
            preferences,
            cancellationToken);
    }

    public async Task<PreparedPreviewStart> ValidateAndPreparePreviewAsync(
        AcquisitionPreviewStartInput input,
        CancellationToken cancellationToken = default)
    {
        var project = input.Project ?? throw new InvalidOperationException("请先从项目列表选择项目，再进入采集。");
        var montage = input.Montage ?? throw new InvalidOperationException("请先在采集准备页选择导联配置。");
        var channelSnapshot = montage.ChannelConfigurationSnapshot;
        var device = await deviceSession.ConfirmSelectedDeviceAvailabilityAsync(cancellationToken);
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

        var samplingRateHz = input.SamplingRateHz ?? throw new InvalidOperationException("请选择采样率。");
        var filterSettings = new LiveDisplayFilterSettings(
            input.DisplayPreferences.HighPassHz,
            input.DisplayPreferences.LowPassHz,
            input.DisplayPreferences.NotchHz <= 0 ? null : input.DisplayPreferences.NotchHz);
        filterSettings.Validate();
        if (input.DisplayPreferences.LowPassHz >= samplingRateHz / 2d)
        {
            throw new InvalidOperationException("低通必须低于当前采样率的一半（奈奎斯特频率）。");
        }

        if (input.DisplayPreferences.NotchHz > 0 && input.DisplayPreferences.NotchHz >= samplingRateHz / 2d)
        {
            throw new InvalidOperationException("陷波必须低于当前采样率的一半（奈奎斯特频率）。");
        }

        var referenceRange = input.ReferenceRangeVolts ?? throw new InvalidOperationException("请选择参考量程。");
        var bipolarRange = input.BipolarRangeVolts ?? throw new InvalidOperationException("请选择双极量程。");
        AntEegoRangePair.Validate(referenceRange, bipolarRange);

        var historicalProfiles = (await montageStore.LoadAsync(cancellationToken))
            .Where(profile => !profile.IsArchived && string.Equals(profile.ChannelConfigurationFingerprint, montage.ChannelConfigurationFingerprint, StringComparison.Ordinal))
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (historicalProfiles.All(profile => profile.Id != montage.Id))
        {
            historicalProfiles = [montage, .. historicalProfiles];
        }

        if (string.IsNullOrWhiteSpace(project.DirectoryPath))
        {
            throw new InvalidOperationException("所选项目没有有效的项目目录，请先打开项目设置修正。");
        }

        new RecordingSubject(
            input.SubjectCode,
            input.SubjectName,
            input.SubjectId,
            input.SubjectSex,
            input.AgeAtRecordingYears).Validate();

        var projectContext = new AcquisitionProjectContext(
            project.Id,
            project.Number,
            project.Name,
            project.DirectoryPath,
            JsonSerializer.Serialize(project),
            input.SubjectCode.Trim(),
            input.SubjectName.Trim(),
            input.SubjectId,
            input.SubjectSex,
            input.AgeAtRecordingYears);

        return new PreparedPreviewStart(
            input,
            project,
            montage,
            channelSnapshot,
            device,
            samplingRateHz,
            referenceRange,
            bipolarRange,
            input.DisplayPreferences,
            filterSettings,
            input.SdkLibraryPath.Trim(),
            input.RecordingDirectory.Trim(),
            historicalProfiles,
            projectContext);
    }

    public static bool HasInputChanged(AcquisitionPreviewStartInput before, AcquisitionPreviewStartInput after) =>
        !ReferenceEquals(before.Project, after.Project) ||
        !ReferenceEquals(before.Montage, after.Montage) ||
        before.SamplingRateHz != after.SamplingRateHz ||
        before.ReferenceRangeVolts != after.ReferenceRangeVolts ||
        before.BipolarRangeVolts != after.BipolarRangeVolts ||
        before.DisplayPreferences != after.DisplayPreferences ||
        !string.Equals(before.SdkLibraryPath, after.SdkLibraryPath, StringComparison.Ordinal) ||
        !string.Equals(before.RecordingDirectory, after.RecordingDirectory, StringComparison.Ordinal) ||
        !string.Equals(before.SubjectCode, after.SubjectCode, StringComparison.Ordinal) ||
        !string.Equals(before.SubjectName, after.SubjectName, StringComparison.Ordinal) ||
        !string.Equals(before.SubjectId, after.SubjectId, StringComparison.Ordinal) ||
        !string.Equals(before.SubjectSex, after.SubjectSex, StringComparison.Ordinal) ||
        before.AgeAtRecordingYears != after.AgeAtRecordingYears;

    public async Task StartPreparedStreamAsync(
        PreparedPreviewStart prepared,
        IReadOnlyDictionary<int, string> channelLabelsByNativeIndex,
        Action<int, double, double> recordPersistedSelection,
        Action ensureInputUnchanged,
        CancellationToken cancellationToken = default)
    {
        await deviceSession.ConfigureForStreamAsync(
            AntEegoAcquisitionDriver.CreateConfiguration(new AntEegoAdapterOptions(
                prepared.SdkLibraryPath,
                prepared.ReferenceRangeVolts,
                prepared.BipolarRangeVolts,
                ChannelLabelsByNativeIndex: channelLabelsByNativeIndex,
                HardwareReferenceElectrodeLocation: prepared.ChannelSnapshot.HardwareReferenceElectrodeLocation,
                HardwareGroundElectrodeLocation: prepared.ChannelSnapshot.HardwareGroundElectrodeLocation,
                ChannelConfigurationId: prepared.ChannelSnapshot.Id,
                ChannelConfigurationName: prepared.ChannelSnapshot.Name,
                ChannelConfigurationSnapshotJson: JsonSerializer.Serialize(prepared.ChannelSnapshot),
                MontageConfigurationId: prepared.Montage.Id,
                MontageConfigurationName: prepared.Montage.Name,
                MontageConfigurationSnapshotJson: JsonSerializer.Serialize(prepared.Montage),
                MontageCatalogueSnapshotJson: JsonSerializer.Serialize(prepared.HistoricalProfiles))),
            cancellationToken);

        ensureInputUnchanged();
        recordPersistedSelection(prepared.SamplingRateHz, prepared.ReferenceRangeVolts, prepared.BipolarRangeVolts);
        await SaveSettingsAsync(
            prepared.SdkLibraryPath,
            prepared.RecordingDirectory,
            prepared.SamplingRateHz,
            prepared.ReferenceRangeVolts.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            prepared.BipolarRangeVolts.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            prepared.DisplayPreferences,
            cancellationToken);

        ensureInputUnchanged();
        await runtime.StartPreviewAsync(
            new AcquisitionStreamRequest(prepared.Device.DeviceId, prepared.SamplingRateHz, prepared.ProjectContext),
            prepared.Montage,
            cancellationToken);
    }

    public async Task<ResearchProject> StartRecordingAsync(
        ResearchProject? selectedProject,
        CancellationToken cancellationToken = default)
    {
        var project = selectedProject ?? throw new InvalidOperationException("当前预览没有关联项目。");
        await runtime.StartRecordingAsync(cancellationToken);
        return project;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        runtime.PauseAsync(cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        runtime.ResumeAsync(cancellationToken);

    public async Task<StoppedAcquisitionOutcome> StopAndRegisterAsync(
        Func<Task> closeImpedanceAsync,
        CancellationToken cancellationToken = default)
    {
        await closeImpedanceAsync();
        var stoppedState = runtime.State.State;
        var completedDirectory = runtime.RecordingDirectory;
        await runtime.StopAsync(cancellationToken);

        var hadRecording = stoppedState is AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;
        RegisteredRecording? registered = null;
        string? warning = null;
        if (hadRecording && recordingRegistrationClient is not null && completedDirectory is not null)
        {
            try
            {
                registered = await recordingRegistrationClient.RegisterWpfRecordingAsync(
                    completedDirectory,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                // Registration must never turn a successfully persisted raw recording into a failed acquisition.
                warning = $"原始记录已保存，但尚未注册到分析服务：{exception.Message}";
            }
        }

        return new StoppedAcquisitionOutcome(
            hadRecording,
            registered,
            warning,
            runtime.State.Detail,
            runtime.State.ChangedAtUtc);
    }
}

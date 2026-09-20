using System.Collections.ObjectModel;
using System.Windows.Input;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Session;

namespace BrainPlatform.Desktop.ViewModels;

/// <summary>Coordinates user presets with the device-validated live mapping.</summary>
public sealed class ChannelConfigurationWorkspaceViewModel : ObservableObject
{
    private readonly ChannelMappingViewModel mapping;
    private readonly ChannelConfigurationProfileStore store;
    private readonly ChannelConfigurationReferencePolicy referencePolicy;
    private readonly DeviceSessionManager? deviceSession;
    private readonly OperationNotificationCenter? notifications;
    private ChannelConfigurationProfile? selectedProfile;
    private string? observedDeviceSignature;
    private string statusText = "请先连接放大器，读取实际物理通道后再管理通道配置。";
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private string draftReferenceElectrodeLocation = "REF";
    private string draftGroundElectrodeLocation = "GND";
    private AcquisitionDeviceDescriptor? draftDevice;
    private ChannelConfigurationProfile? draftSourceProfile;
    private string? draftProfileId;
    private DateTimeOffset? draftCreatedAtUtc;
    private bool isDraftOpen;
    private bool isReadOnlyDraft;
    private bool isSignalLockedDraft;
    private bool isCreatingVersion;

    public ChannelConfigurationWorkspaceViewModel(
        ChannelMappingViewModel mapping,
        ChannelConfigurationProfileStore? store = null,
        DeviceSessionManager? deviceSession = null,
        MontageProfileStore? montageStore = null,
        OperationNotificationCenter? notifications = null)
    {
        this.mapping = mapping;
        this.store = store ?? new ChannelConfigurationProfileStore();
        referencePolicy = new ChannelConfigurationReferencePolicy(montageStore);
        this.deviceSession = deviceSession;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCommandError);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, ReportCommandError);
        CloseDraftCommand = new AsyncRelayCommand(() => { CloseDraft(); return Task.CompletedTask; }, ReportCommandError);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportCommandError);
        mapping.DisplayChannelsChanged += OnMappingChanged;
    }

    public ObservableCollection<ChannelConfigurationProfile> Profiles { get; } = [];

    public event EventHandler? ProfilesChanged;

    public ICommand RefreshCommand { get; }

    public ICommand SaveDraftCommand { get; }

    public ICommand CloseDraftCommand { get; }

    public ICommand DeleteSelectedCommand { get; }

    public ChannelConfigurationProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                RaisePropertyChanged(nameof(CanDeleteSelected));
                RaisePropertyChanged(nameof(CanCreateVersionSelected));
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool CanCreateProfile => mapping.HasLoadedDevice;

    public string? CurrentDeviceSignature => mapping.HasLoadedDevice ? mapping.GetDeviceSignature() : null;

    public bool CanDeleteSelected => SelectedProfile?.CanDelete == true;

    public bool CanCreateVersionSelected => SelectedProfile?.CanCreateVersion == true;

    public ObservableCollection<ChannelConfigurationDraftRow> DraftRows { get; } = [];

    public AcquisitionDeviceDescriptor? DraftDevice
    {
        get => draftDevice;
        set
        {
            if (SetProperty(ref draftDevice, value))
            {
                RaisePropertyChanged(nameof(IsDevicePickerVisible));
                if (value is not null)
                {
                    if (deviceSession is not null &&
                        !string.Equals(deviceSession.SelectedDevice?.DeviceId, value.DeviceId, StringComparison.Ordinal))
                    {
                        deviceSession.SelectDevice(value);
                    }
                    BuildDraftRows(deviceSession?.SelectedDevice ?? value, draftSourceProfile);
                }
            }
        }
    }

    public bool IsDraftOpen
    {
        get => isDraftOpen;
        private set
        {
            if (SetProperty(ref isDraftOpen, value))
            {
                RaisePropertyChanged(nameof(IsDevicePickerVisible));
                RaisePropertyChanged(nameof(CanEditDraft));
                RaisePropertyChanged(nameof(DraftTitle));
            }
        }
    }

    public bool IsDevicePickerVisible => IsDraftOpen && CanEditSignalDraft && DraftDevice is null;

    public bool CanEditDraft => IsDraftOpen && !isReadOnlyDraft;

    public bool CanEditSignalDraft => CanEditDraft && !isSignalLockedDraft;

    public bool IsSignalLockedDraft => isSignalLockedDraft;

    public string DraftLockMessage => isSignalLockedDraft && draftSourceProfile is not null
        ? $"该配置已被 {draftSourceProfile.MontageReferenceCount} 个导联配置引用。电极映射、显示顺序及 REF/GND 已锁定；名称和说明仍可修改。"
        : string.Empty;

    public string DraftTitle => CanEditDraft
        ? isCreatingVersion ? "创建通道配置新版本" : draftProfileId is null ? "新建通道配置" : "编辑通道配置"
        : "查看通道配置";

    public string DraftName
    {
        get => draftName;
        set => SetProperty(ref draftName, value);
    }

    public string DraftDescription
    {
        get => draftDescription;
        set => SetProperty(ref draftDescription, value);
    }

    public int DraftEegInputCount => DraftRows.Count(row => row.IsEegInput);

    public int DraftEnabledCount => DraftRows.Count(row => row.IsEegInput && row.IsEnabled);

    /// <summary>Operator-recorded scalp location of the fixed hardware REF lead.</summary>
    public string DraftReferenceElectrodeLocation
    {
        get => draftReferenceElectrodeLocation;
        set => SetProperty(ref draftReferenceElectrodeLocation, value);
    }

    /// <summary>Operator-recorded scalp location of the fixed hardware GND lead.</summary>
    public string DraftGroundElectrodeLocation
    {
        get => draftGroundElectrodeLocation;
        set => SetProperty(ref draftGroundElectrodeLocation, value);
    }

    public void BeginNewProfile()
    {
        isReadOnlyDraft = false;
        isSignalLockedDraft = false;
        isCreatingVersion = false;
        SelectedProfile = null;
        draftSourceProfile = null;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftReferenceElectrodeLocation = "REF";
        DraftGroundElectrodeLocation = "GND";
        DraftDevice = null;
        DraftRows.Clear();
        IsDraftOpen = true;
        StatusText = "请选择设备；软件会读取实际物理输入并在设备布局匹配时套用默认电极模板。";
        RaiseDraftModePropertiesChanged();
    }

    public void BeginEditSelected()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var profile = SelectedProfile;
        draftSourceProfile = profile;
        isReadOnlyDraft = profile.Source == ChannelConfigurationSource.System;
        isSignalLockedDraft = profile.IsSignalLocked;
        isCreatingVersion = false;
        draftProfileId = profile.Source == ChannelConfigurationSource.User ? profile.Id : null;
        draftCreatedAtUtc = profile.Source == ChannelConfigurationSource.User ? profile.CreatedAtUtc : null;
        DraftName = profile.Name;
        DraftDescription = profile.Description;
        DraftReferenceElectrodeLocation = DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF");
        DraftGroundElectrodeLocation = DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND");
        DraftDevice = null;
        DraftRows.Clear();
        IsDraftOpen = true;
        RaiseDraftModePropertiesChanged();
        var device = deviceSession?.SelectedDevice;
        if (device is not null)
        {
            DraftDevice = device;
        }
        else if (mapping.HasLoadedDevice)
        {
            BuildDraftRowsFromCurrentDevice(profile);
        }
    }

    public void BeginNewVersionSelected()
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("请先选择一份通道配置。");
        if (!mapping.HasLoadedDevice ||
            !string.Equals(profile.DeviceSignature, mapping.GetDeviceSignature(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("请连接与该通道配置兼容的设备后再创建新版本。");
        }

        draftSourceProfile = profile;
        isReadOnlyDraft = false;
        isSignalLockedDraft = false;
        isCreatingVersion = true;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = $"{profile.Name} 新版本";
        DraftDescription = profile.Description;
        DraftReferenceElectrodeLocation = DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF");
        DraftGroundElectrodeLocation = DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND");
        DraftDevice = null;
        DraftRows.Clear();
        IsDraftOpen = true;
        RaiseDraftModePropertiesChanged();
        BuildDraftRowsFromCurrentDevice(profile);
        StatusText = $"已基于“{profile.Name}”创建可编辑的新版本草稿；原配置及其导联引用不会改变。";
    }

    public void CloseDraft()
    {
        IsDraftOpen = false;
        DraftDevice = null;
        DraftRows.Clear();
        draftSourceProfile = null;
        isReadOnlyDraft = false;
        isSignalLockedDraft = false;
        isCreatingVersion = false;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftReferenceElectrodeLocation = "REF";
        DraftGroundElectrodeLocation = "GND";
        RaiseDraftModePropertiesChanged();
    }

    public async Task RefreshAsync()
    {
        var selectedId = SelectedProfile?.Id;
        var referenceCounts = await referencePolicy.GetReferenceCountsAsync(CancellationToken.None);
        Profiles.Clear();
        if (mapping.TryCreateDefaultSystemProfile(out var systemProfile))
        {
            var systemReferenceCount = SystemMontageProfileFactory.Create(systemProfile).Count;
            Profiles.Add(PrepareForCurrentDevice(
                systemProfile,
                checked(referenceCounts.GetValueOrDefault(systemProfile.Id) + systemReferenceCount)));
        }

        foreach (var profile in await store.LoadAsync(CancellationToken.None))
        {
            Profiles.Add(PrepareForCurrentDevice(profile, referenceCounts.GetValueOrDefault(profile.Id)));
        }

        SelectedProfile = selectedId is null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(profile => profile.Id == selectedId);

        StatusText = mapping.HasLoadedDevice
            ? $"已载入 {Profiles.Count} 个通道配置。当前设备有 {mapping.ActualEegInputCount} 个可标注 EEG 输入。"
            : "请先连接放大器；系统只会向导联配置提供与当前设备兼容的通道配置。";
        RaisePropertyChanged(nameof(CanCreateProfile));
        RaisePropertyChanged(nameof(CanCreateVersionSelected));
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMappingChanged(object? sender, EventArgs eventArgs)
    {
        RaisePropertyChanged(nameof(CanCreateProfile));
        var currentSignature = mapping.HasLoadedDevice ? mapping.GetDeviceSignature() : null;
        if (string.Equals(observedDeviceSignature, currentSignature, StringComparison.Ordinal))
        {
            return;
        }

        observedDeviceSignature = currentSignature;
        _ = RefreshAsync();
    }

    private ChannelConfigurationProfile PrepareForCurrentDevice(ChannelConfigurationProfile profile, int montageReferenceCount)
    {
        if (!mapping.HasLoadedDevice)
        {
            return profile with
            {
                MontageReferenceCount = montageReferenceCount,
                IsCompatibleWithCurrentDevice = false,
                StatusLabel = "待连接",
                StatusDetail = "连接设备后验证此配置是否可用。",
            };
        }

        var matchesCurrentDevice = string.Equals(profile.DeviceSignature, mapping.GetDeviceSignature(), StringComparison.Ordinal);
        return profile with
        {
            IsActive = false,
            HasUnappliedChanges = false,
            MontageReferenceCount = montageReferenceCount,
            IsCompatibleWithCurrentDevice = matchesCurrentDevice,
            StatusLabel = matchesCurrentDevice ? "可用于导联" : "设备不匹配",
            StatusDetail = matchesCurrentDevice
                ? "配置有效且与当前设备兼容，可在导联配置中选择。"
                : "当前设备的物理输入或通道类型与此配置不匹配。",
        };
    }

    private void BuildDraftRows(AcquisitionDeviceDescriptor device, ChannelConfigurationProfile? profile)
    {
        DraftRows.Clear();
        var capabilities = device.ChannelCapabilities ?? [];
        var eeg = capabilities.Where(IsEeg).ToArray();
        var hasDefault = DefaultChannelLabelTemplate.TryGetFor(eeg, out var defaults);
        var entries = profile?.Channels.ToDictionary(entry => entry.NativeChannelIndex) ?? [];
        foreach (var capability in capabilities.Where(IsEeg).OrderBy(item => item.NativeChannelIndex))
        {
            entries.TryGetValue(capability.NativeChannelIndex, out var entry);
            var isEeg = IsEeg(capability);
            var label = entry?.ElectrodeLabel ?? (hasDefault ? defaults.GetValueOrDefault(capability.NativeChannelIndex) ?? string.Empty : string.Empty);
            var enabled = entry?.IsSelectedForDisplay ?? (isEeg && !string.IsNullOrWhiteSpace(label));
            var row = new ChannelConfigurationDraftRow(
                capability.NativeChannelIndex,
                capability.Kind.ToString(),
                capability.Unit,
                isEeg ? "µV" : capability.Unit,
                label,
                enabled,
                entry?.DisplayOrder ?? capability.NativeChannelIndex,
                isEeg);
            row.PropertyChanged += OnDraftRowPropertyChanged;
            DraftRows.Add(row);
        }
        RaiseDraftCounts();
    }

    private void OnDraftRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ChannelConfigurationDraftRow.IsEnabled)
            or nameof(ChannelConfigurationDraftRow.ElectrodeLabel))
        {
            if (eventArgs.PropertyName == nameof(ChannelConfigurationDraftRow.IsEnabled) &&
                sender is ChannelConfigurationDraftRow row)
            {
                var identity = string.IsNullOrWhiteSpace(row.ElectrodeLabel)
                    ? $"设备输入 {row.NativeChannelIndex}"
                    : $"通道“{row.ElectrodeLabel.Trim()}”";
                notifications?.PublishSuccess(row.IsEnabled
                    ? $"已设为显示{identity}。"
                    : $"已关闭显示{identity}。");
            }
            RaiseDraftCounts();
        }
    }

    private void BuildDraftRowsFromCurrentDevice(ChannelConfigurationProfile profile)
    {
        if (deviceSession?.SelectedDevice is { } selectedDevice)
        {
            DraftDevice = selectedDevice;
            return;
        }

        var current = mapping.AllRows.Select(row => new AcquisitionChannelCapability(
            row.NativeChannelIndex,
            Enum.Parse<AcquisitionChannelKind>(row.Role),
            row.Unit)).ToArray();
        var snapshot = new AcquisitionDeviceDescriptor(
            mapping.CurrentDeviceKey ?? string.Empty,
            mapping.CurrentDeviceDisplayName ?? string.Empty,
            null,
            [],
            ChannelCapabilities: current,
            Model: mapping.CurrentDeviceModel,
            DriverId: mapping.CurrentDeviceDriverId);
        SetProperty(ref draftDevice, snapshot);
        RaisePropertyChanged(nameof(IsDevicePickerVisible));
        BuildDraftRows(snapshot, profile);
    }

    public async Task SaveDraftAsync()
    {
        if (!CanEditDraft)
        {
            throw new InvalidOperationException("系统默认通道配置只能查看，不能修改。");
        }
        if (DraftDevice is null || DraftRows.Count == 0)
        {
            throw new InvalidOperationException("请先选择设备并读取设备通道。");
        }
        var name = DraftName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("配置名称不能为空。");
        }
        var now = DateTimeOffset.UtcNow;
        var entries = DraftRows.Where(row => row.IsEegInput).Select(row => new ChannelConfigurationEntry(
            row.NativeChannelIndex,
            Enum.Parse<AcquisitionChannelKind>(row.Role),
            row.ElectrodeLabel.Trim(),
            row.IsEnabled,
            row.DisplayOrder)).ToArray();
        var profile = new ChannelConfigurationProfile(
            draftProfileId ?? Guid.NewGuid().ToString("N"),
            name,
            DraftDescription.Trim(),
            ChannelConfigurationSource.User,
            AcquisitionDeviceCompatibility.CreateSignature(DraftDevice.DriverId, DraftDevice.ChannelCapabilities),
            entries,
            draftCreatedAtUtc ?? now,
            now,
            DraftDevice.DisplayName,
            AcquisitionDeviceCompatibility.NormalizeDriverId(DraftDevice.DriverId),
            DraftDevice.Model ?? string.Empty,
            NormalizeLocation(DraftReferenceElectrodeLocation),
            NormalizeLocation(DraftGroundElectrodeLocation));
        ChannelConfigurationValidation.Validate(profile);
        if (draftSourceProfile is { Source: ChannelConfigurationSource.User } original &&
            string.Equals(original.Id, profile.Id, StringComparison.Ordinal))
        {
            await referencePolicy.EnsureSignalChangeAllowedAsync(original, profile, CancellationToken.None);
        }
        await store.SaveAsync(profile, CancellationToken.None);
        await RefreshAsync();
        SelectedProfile = Profiles.FirstOrDefault(item =>
            item.Id == profile.Id && item.Source == ChannelConfigurationSource.User)
            ?? throw new InvalidOperationException("保存后未找到用户通道配置。");
        IsDraftOpen = false;
        StatusText = $"已保存通道配置“{profile.Name}”。";
        notifications?.PublishSuccess($"已保存通道配置“{profile.Name}”。请点击“应用”后用于采集。");
    }

    private static bool IsEeg(AcquisitionChannelCapability capability) =>
        capability.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar;

    private static string NormalizeLocation(string value) => value.Trim();

    private static string DefaultLocation(string? location, string fallback) =>
        string.IsNullOrWhiteSpace(location) ? fallback : location.Trim();

    private void RaiseDraftCounts()
    {
        RaisePropertyChanged(nameof(DraftEegInputCount));
        RaisePropertyChanged(nameof(DraftEnabledCount));
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is not { CanDelete: true } profile)
        {
            throw new InvalidOperationException("系统默认或已被导联引用的通道配置不能删除。");
        }

        await referencePolicy.EnsureDeleteAllowedAsync(profile, CancellationToken.None);
        await store.DeleteAsync(profile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshAsync();
        StatusText = $"已删除通道配置“{profile.Name}”。";
        notifications?.PublishSuccess($"已删除通道配置“{profile.Name}”。");
    }

    private void ReportCommandError(Exception exception)
    {
        StatusText = exception.Message;
        notifications?.PublishError(exception.Message);
    }

    private void RaiseDraftModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsDevicePickerVisible));
        RaisePropertyChanged(nameof(CanEditDraft));
        RaisePropertyChanged(nameof(CanEditSignalDraft));
        RaisePropertyChanged(nameof(IsSignalLockedDraft));
        RaisePropertyChanged(nameof(DraftLockMessage));
        RaisePropertyChanged(nameof(DraftTitle));
    }
}

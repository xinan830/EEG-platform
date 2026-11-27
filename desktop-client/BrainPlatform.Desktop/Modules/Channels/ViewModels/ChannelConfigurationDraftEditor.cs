using System.Collections.ObjectModel;
using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

/// <summary>
/// Sole owner of the active channel-configuration draft, device-backed draft rows,
/// saved baseline comparison, and row property subscriptions.
/// </summary>
internal sealed class ChannelConfigurationDraftEditor : ObservableObject
{
    private readonly ChannelMappingViewModel mapping;
    private readonly DeviceSessionManager? deviceSession;
    private readonly Action<string>? notifySuccess;
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private string draftReferenceElectrodeLocation = "REF";
    private string draftGroundElectrodeLocation = "GND";
    private AcquisitionDeviceDescriptor? draftDevice;
    private AcquisitionDeviceDescriptor? pendingDeviceSelection;
    private ChannelConfigurationProfile? draftSourceProfile;
    private string? draftProfileId;
    private DateTimeOffset? draftCreatedAtUtc;
    private bool isDraftOpen;
    private bool isReadOnlyDraft;
    private bool isSignalLockedDraft;
    private bool isCreatingVersion;
    private bool isDevicePickerVisible;
    private string channelSearchText = string.Empty;
    private bool isBulkDisplayUpdate;
    private ChannelConfigurationProfile? savedDraftBaseline;

    public ChannelConfigurationDraftEditor(
        ChannelMappingViewModel mapping,
        DeviceSessionManager? deviceSession,
        Action<string>? notifySuccess)
    {
        this.mapping = mapping;
        this.deviceSession = deviceSession;
        this.notifySuccess = notifySuccess;
        DraftHardwareLandmarks.PropertyChanged += OnHardwareLandmarksChanged;
    }

    public long SessionVersion { get; private set; }
    public ObservableCollection<ChannelConfigurationDraftRow> DraftRows { get; } = [];
    public ElectrodeMapLandmarkDraft DraftHardwareLandmarks { get; } = new();
    public ObservableCollection<ChannelConfigurationDraftRow> FilteredDraftRows { get; } = [];
    public ChannelConfigurationProfile? DraftSourceProfile => draftSourceProfile;
    public bool IsDevicePickerVisible => isDevicePickerVisible;
    public bool IsSignalLockedDraft => isSignalLockedDraft;
    public bool CanEditDraft => IsDraftOpen && !isReadOnlyDraft;
    public bool CanEditSignalDraft => CanEditDraft && !isSignalLockedDraft;
    public bool HasPendingDeviceSelection => PendingDeviceSelection is not null;
    public bool HasUnsavedChanges => CanEditDraft && savedDraftBaseline is not null && !IsDraftEquivalentTo(savedDraftBaseline);
    public int DraftEegInputCount => DraftRows.Count(row => row.IsEegInput);
    public int DraftEnabledCount => DraftRows.Count(row => row.IsEegInput && row.IsEnabled);
    public string DraftSourceLabel => isReadOnlyDraft && draftSourceProfile?.Source == ChannelConfigurationSource.System ? "系统默认" : "用户创建";
    public string DraftDeviceVendor => DeviceProductPresentation.ForDriver(DraftDevice?.DriverId ?? draftSourceProfile?.DeviceDriverId, "未选择设备");
    public string DraftDeviceModel => DraftDevice?.Model ?? draftSourceProfile?.DeviceModel ?? "设备未提供型号";
    public int DraftMontageReferenceCount => draftProfileId is null && !isReadOnlyDraft ? 0 : draftSourceProfile?.MontageReferenceCount ?? 0;
    public string DraftCreatedAtText => draftCreatedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "保存后生成";
    public string DraftUpdatedAtText => draftProfileId is null ? "保存后生成" : draftSourceProfile?.UpdatedAtText ?? "保存后生成";
    public string DraftStatusLabel => isReadOnlyDraft ? "系统默认" : isSignalLockedDraft ? "被导联引用" : "可编辑";
    public string DraftLockMessage => isSignalLockedDraft && draftSourceProfile is not null
        ? $"该配置已被 {draftSourceProfile.MontageReferenceCount} 个导联配置引用。电极映射、显示顺序及 REF/GND 已锁定；名称和说明仍可修改。"
        : string.Empty;
    public string DraftTitle => CanEditDraft
        ? isCreatingVersion ? "创建通道配置新版本" : draftProfileId is null ? "新建通道配置" : "编辑通道配置"
        : "查看通道配置";

    public string ChannelSearchText
    {
        get => channelSearchText;
        set { if (SetProperty(ref channelSearchText, value)) ApplyDraftRowFilter(); }
    }

    public AcquisitionDeviceDescriptor? DraftDevice
    {
        get => draftDevice;
        set
        {
            if (SetProperty(ref draftDevice, value))
            {
                RaisePropertyChanged(nameof(DraftDeviceVendor));
                RaisePropertyChanged(nameof(DraftDeviceModel));
                if (value is not null)
                {
                    SetDevicePickerVisible(false);
                    if (deviceSession is not null &&
                        !string.Equals(deviceSession.SelectedDevice?.DeviceId, value.DeviceId, StringComparison.Ordinal))
                    {
                        deviceSession.SelectDevice(value);
                    }
                    BuildDraftRows(deviceSession?.SelectedDevice ?? value, draftSourceProfile);
                    if (savedDraftBaseline is null && (draftSourceProfile is null || isCreatingVersion))
                    {
                        savedDraftBaseline = CreateProfileSnapshot(DateTimeOffset.UtcNow);
                        RaisePropertyChanged(nameof(HasUnsavedChanges));
                    }
                }
            }
        }
    }

    public AcquisitionDeviceDescriptor? PendingDeviceSelection
    {
        get => pendingDeviceSelection;
        set { if (SetProperty(ref pendingDeviceSelection, value)) RaisePropertyChanged(nameof(HasPendingDeviceSelection)); }
    }

    public bool IsDraftOpen
    {
        get => isDraftOpen;
        private set
        {
            if (SetProperty(ref isDraftOpen, value))
            {
                RaisePropertyChanged(nameof(CanEditDraft));
                RaisePropertyChanged(nameof(DraftTitle));
            }
        }
    }

    public string DraftName { get => draftName; set { if (SetProperty(ref draftName, value)) RaisePropertyChanged(nameof(HasUnsavedChanges)); } }
    public string DraftDescription { get => draftDescription; set { if (SetProperty(ref draftDescription, value)) RaisePropertyChanged(nameof(HasUnsavedChanges)); } }
    public string DraftReferenceElectrodeLocation { get => draftReferenceElectrodeLocation; set { if (SetProperty(ref draftReferenceElectrodeLocation, value)) RaisePropertyChanged(nameof(HasUnsavedChanges)); } }
    public string DraftGroundElectrodeLocation { get => draftGroundElectrodeLocation; set { if (SetProperty(ref draftGroundElectrodeLocation, value)) RaisePropertyChanged(nameof(HasUnsavedChanges)); } }

    public void BeginNew(bool showDevicePicker)
    {
        ResetDraftFields(null, readOnly: false, signalLocked: false, creatingVersion: false,
            profileId: null, createdAt: null, name: string.Empty, description: string.Empty,
            refLoc: "REF", gndLoc: "GND", loadProfile: false, showDevicePicker: showDevicePicker);
    }

    public void BeginEdit(ChannelConfigurationProfile profile)
    {
        var isUser = profile.Source == ChannelConfigurationSource.User;
        ResetDraftFields(profile, readOnly: profile.Source == ChannelConfigurationSource.System,
            signalLocked: profile.IsSignalLocked, creatingVersion: false,
            profileId: isUser ? profile.Id : null, createdAt: isUser ? profile.CreatedAtUtc : null,
            name: profile.DisplayName, description: profile.DisplayDescription,
            refLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF"),
            gndLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND"),
            loadProfile: true, showDevicePicker: null);
        if (deviceSession?.SelectedDevice is { } device) DraftDevice = device;
        else if (mapping.HasLoadedDevice) BuildDraftRowsFromCurrentDevice(profile);
        savedDraftBaseline = isUser ? profile : null;
        if (isUser && CanCreateProfileBaseline())
            savedDraftBaseline = ChannelDraftMapper.CreateSavedBaseline(profile, CreateProfileSnapshot(profile.UpdatedAtUtc));
        RaisePropertyChanged(nameof(HasUnsavedChanges));
    }

    public void BeginNewVersion(ChannelConfigurationProfile profile)
    {
        ResetDraftFields(profile, readOnly: false, signalLocked: false, creatingVersion: true,
            profileId: null, createdAt: null, name: $"{profile.DisplayName} 新版本",
            description: profile.DisplayDescription,
            refLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF"),
            gndLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND"),
            loadProfile: true, showDevicePicker: null);
        BuildDraftRowsFromCurrentDevice(profile);
        savedDraftBaseline = CanCreateProfileBaseline() ? CreateProfileSnapshot(DateTimeOffset.UtcNow) : null;
        RaisePropertyChanged(nameof(HasUnsavedChanges));
    }

    public void BeginCopy(ChannelConfigurationProfile profile)
    {
        ResetDraftFields(profile, readOnly: false, signalLocked: false, creatingVersion: false,
            profileId: null, createdAt: null, name: $"{profile.DisplayName} 副本",
            description: profile.DisplayDescription,
            refLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareReferenceElectrodeLocation, "REF"),
            gndLoc: ChannelDraftMapper.DefaultLocation(profile.HardwareGroundElectrodeLocation, "GND"),
            loadProfile: true, showDevicePicker: null, clearChannelSearch: true);
        if (deviceSession?.SelectedDevice is { } device) DraftDevice = device;
        else if (mapping.HasLoadedDevice &&
                 string.Equals(profile.DeviceSignature, mapping.GetDeviceSignature(), StringComparison.Ordinal))
        {
            BuildDraftRowsFromCurrentDevice(profile);
        }
        savedDraftBaseline = CanCreateProfileBaseline() ? CreateProfileSnapshot(DateTimeOffset.UtcNow) : null;
        RaisePropertyChanged(nameof(HasUnsavedChanges));
    }

    public void Close()
    {
        SessionVersion++;
        SetDevicePickerVisible(false);
        PendingDeviceSelection = null;
        IsDraftOpen = false;
        savedDraftBaseline = null;
        DraftDevice = null;
        ClearDraftRows();
        draftSourceProfile = null;
        isReadOnlyDraft = isSignalLockedDraft = isCreatingVersion = false;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftReferenceElectrodeLocation = "REF";
        DraftGroundElectrodeLocation = "GND";
        ResetMapPositionsToDefaults();
        RaiseDraftModePropertiesChanged();
    }

    public AcquisitionDeviceDescriptor ConfirmPendingDeviceSelection()
    {
        if (!CanEditSignalDraft) throw new InvalidOperationException("当前通道配置不允许修改设备输入。");
        var selected = PendingDeviceSelection ?? throw new InvalidOperationException("请选择一个已检测到的设备后再确认。");
        if (deviceSession is not null &&
            !deviceSession.Devices.Any(device => string.Equals(device.DeviceId, selected.DeviceId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("所选设备已不在当前检测列表中，请重新检测后选择。");
        }
        DraftDevice = selected;
        PendingDeviceSelection = null;
        return selected;
    }

    public void CancelDeviceSelection() { PendingDeviceSelection = null; SetDevicePickerVisible(false); }
    public void ResetMapPositionsToDefaults() => DraftHardwareLandmarks.Reset();

    public void SetAllEegChannelsDisplayed(bool isDisplayed)
    {
        if (!CanEditSignalDraft) throw new InvalidOperationException("当前通道配置不允许修改显示通道。");
        var rowsToChange = DraftRows.Where(row => row.IsEegInput && row.IsEnabled != isDisplayed).ToArray();
        if (rowsToChange.Length == 0) return;

        isBulkDisplayUpdate = true;
        try { foreach (var row in rowsToChange) row.IsEnabled = isDisplayed; }
        finally { isBulkDisplayUpdate = false; }

        RaiseDraftCounts();
        notifySuccess?.Invoke(isDisplayed
            ? $"已全部显示 {rowsToChange.Length} 个 EEG 通道。"
            : $"已全部关闭显示 {rowsToChange.Length} 个 EEG 通道。");
    }

    public ChannelConfigurationProfile CreateProfileSnapshot(DateTimeOffset updatedAt)
    {
        if (DraftDevice is null || DraftRows.Count == 0)
            throw new InvalidOperationException("请先选择设备并读取设备通道。");
        return ChannelDraftMapper.CreateProfileSnapshot(
            DraftDevice, DraftRows, draftProfileId, DraftName, DraftDescription,
            draftCreatedAtUtc, updatedAt, DraftReferenceElectrodeLocation,
            DraftGroundElectrodeLocation, DraftHardwareLandmarks);
    }

    public void MarkSavedBaseline(ChannelConfigurationProfile profile)
    {
        savedDraftBaseline = profile;
        RaisePropertyChanged(nameof(HasUnsavedChanges));
    }

    private void ResetDraftFields(ChannelConfigurationProfile? sourceProfile, bool readOnly, bool signalLocked,
        bool creatingVersion, string? profileId, DateTimeOffset? createdAt, string name, string description,
        string refLoc, string gndLoc, bool loadProfile, bool? showDevicePicker, bool clearChannelSearch = false)
    {
        SessionVersion++;
        draftSourceProfile = sourceProfile;
        isReadOnlyDraft = readOnly;
        isSignalLockedDraft = signalLocked;
        isCreatingVersion = creatingVersion;
        draftProfileId = profileId;
        draftCreatedAtUtc = createdAt;
        DraftName = name;
        DraftDescription = description;
        DraftReferenceElectrodeLocation = refLoc;
        DraftGroundElectrodeLocation = gndLoc;
        if (loadProfile && sourceProfile is not null) DraftHardwareLandmarks.Load(sourceProfile);
        else ResetMapPositionsToDefaults();
        DraftDevice = null;
        ClearDraftRows();
        if (clearChannelSearch) ChannelSearchText = string.Empty;
        savedDraftBaseline = null;
        IsDraftOpen = true;
        if (showDevicePicker is { } pickerVisible)
        {
            PendingDeviceSelection = null;
            SetDevicePickerVisible(pickerVisible);
        }
        RaiseDraftModePropertiesChanged();
    }

    private void BuildDraftRows(AcquisitionDeviceDescriptor device, ChannelConfigurationProfile? profile)
    {
        ClearDraftRows();
        foreach (var row in ChannelDraftMapper.BuildRows(device, profile))
        {
            row.PropertyChanged += OnDraftRowPropertyChanged;
            DraftRows.Add(row);
        }
        RaiseDraftCounts();
        ApplyDraftRowFilter();
        if (IsDraftOpen && profile is not null && profile.Source == ChannelConfigurationSource.User &&
            savedDraftBaseline is null && !isCreatingVersion)
        {
            savedDraftBaseline = ChannelDraftMapper.CreateSavedBaseline(profile, CreateProfileSnapshot(profile.UpdatedAtUtc));
            RaisePropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private void BuildDraftRowsFromCurrentDevice(ChannelConfigurationProfile profile)
    {
        if (deviceSession?.SelectedDevice is { } selectedDevice) { DraftDevice = selectedDevice; return; }
        var snapshot = ChannelDraftMapper.BuildDeviceDescriptor(mapping);
        SetProperty(ref draftDevice, snapshot);
        BuildDraftRows(snapshot, profile);
    }

    private void ClearDraftRows()
    {
        foreach (var row in DraftRows) row.PropertyChanged -= OnDraftRowPropertyChanged;
        DraftRows.Clear();
    }

    private void OnDraftRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        RaisePropertyChanged(nameof(HasUnsavedChanges));
        if (eventArgs.PropertyName == nameof(ChannelConfigurationDraftRow.IsEnabled))
        {
            if (!isBulkDisplayUpdate && sender is ChannelConfigurationDraftRow row)
            {
                var identity = string.IsNullOrWhiteSpace(row.ElectrodeLabel)
                    ? $"设备输入 {row.NativeChannelIndex}" : $"通道“{row.ElectrodeLabel.Trim()}”";
                notifySuccess?.Invoke(row.IsEnabled ? $"已设为显示{identity}。" : $"已关闭显示{identity}。");
            }
            RaiseDraftCounts();
            return;
        }
        if (eventArgs.PropertyName == nameof(ChannelConfigurationDraftRow.ElectrodeLabel))
        {
            RaiseDraftCounts();
            if (!string.IsNullOrWhiteSpace(ChannelSearchText)) ApplyDraftRowFilter();
        }
    }

    private void OnHardwareLandmarksChanged(object? sender, PropertyChangedEventArgs eventArgs) =>
        RaisePropertyChanged(nameof(HasUnsavedChanges));

    private bool CanCreateProfileBaseline() => DraftDevice is not null && DraftRows.Count > 0;

    private bool IsDraftEquivalentTo(ChannelConfigurationProfile baseline) =>
        DraftDevice is null || DraftRows.Count == 0 ||
        ChannelDraftMapper.IsEquivalent(CreateProfileSnapshot(baseline.UpdatedAtUtc), baseline);

    private void ApplyDraftRowFilter()
    {
        var search = ChannelSearchText.Trim();
        FilteredDraftRows.Clear();
        foreach (var row in DraftRows.Where(row =>
                     string.IsNullOrWhiteSpace(search) ||
                     row.NativeChannelIndex.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal) ||
                     row.ElectrodeLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
        {
            FilteredDraftRows.Add(row);
        }
    }

    private void SetDevicePickerVisible(bool value)
    {
        if (isDevicePickerVisible == value) return;
        isDevicePickerVisible = value;
        RaisePropertyChanged(nameof(IsDevicePickerVisible));
    }

    private void RaiseDraftCounts()
    {
        RaisePropertyChanged(nameof(DraftEegInputCount));
        RaisePropertyChanged(nameof(DraftEnabledCount));
    }

    private void RaiseDraftModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(CanEditDraft));
        RaisePropertyChanged(nameof(HasUnsavedChanges));
        RaisePropertyChanged(nameof(CanEditSignalDraft));
        RaisePropertyChanged(nameof(IsSignalLockedDraft));
        RaisePropertyChanged(nameof(DraftLockMessage));
        RaisePropertyChanged(nameof(DraftTitle));
        RaisePropertyChanged(nameof(DraftSourceLabel));
        RaisePropertyChanged(nameof(DraftDeviceVendor));
        RaisePropertyChanged(nameof(DraftDeviceModel));
        RaisePropertyChanged(nameof(DraftMontageReferenceCount));
        RaisePropertyChanged(nameof(DraftCreatedAtText));
        RaisePropertyChanged(nameof(DraftUpdatedAtText));
        RaisePropertyChanged(nameof(DraftStatusLabel));
    }
}

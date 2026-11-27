using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;
using BrainPlatform.Desktop.Modules.Channels.Views;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

/// <summary>Coordinates channel profile persistence, device refresh, and draft/catalogue state.</summary>
public sealed class ChannelConfigurationWorkspaceViewModel : ObservableObject
{
    private readonly ChannelMappingViewModel mapping;
    private readonly ChannelConfigurationProfileStore store;
    private readonly ChannelConfigurationReferencePolicy referencePolicy;
    private readonly IElectrodeLocationClient? electrodeLocationClient;
    private readonly ChannelDraftLocationResolver locationResolver;
    private readonly OperationNotificationCenter? notifications;
    private readonly ChannelProfileCatalogueView catalogue;
    private readonly ChannelConfigurationDraftEditor draft;
    private readonly RelayCommand showAllDraftChannelsCommand;
    private readonly RelayCommand hideAllDraftChannelsCommand;
    private ChannelConfigurationProfile? selectedProfile;
    private string? observedDeviceSignature;
    private long refreshGeneration;
    private string statusText = "请先连接放大器，读取实际物理通道后再管理通道配置。";

    public ChannelConfigurationWorkspaceViewModel(
        ChannelMappingViewModel mapping,
        ChannelConfigurationProfileStore? store = null,
        DeviceSessionManager? deviceSession = null,
        MontageProfileStore? montageStore = null,
        OperationNotificationCenter? notifications = null,
        IElectrodeLocationClient? electrodeLocationClient = null)
    {
        this.mapping = mapping;
        this.store = store ?? new ChannelConfigurationProfileStore();
        referencePolicy = new ChannelConfigurationReferencePolicy(montageStore);
        this.notifications = notifications;
        this.electrodeLocationClient = electrodeLocationClient;
        locationResolver = new ChannelDraftLocationResolver(electrodeLocationClient);
        catalogue = new ChannelProfileCatalogueView(() => this.mapping.HasLoadedDevice);
        draft = new ChannelConfigurationDraftEditor(mapping, deviceSession, message => this.notifications?.PublishSuccess(message));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCommandError);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, ReportCommandError);
        ResolveElectrodeLocationsCommand = new AsyncRelayCommand(ResolveElectrodeLocationsAsync, ReportCommandError);
        CloseDraftCommand = new AsyncRelayCommand(() => { CloseDraft(); return Task.CompletedTask; }, ReportCommandError);
        ConfirmDeviceSelectionCommand = new AsyncRelayCommand(() => { ConfirmPendingDeviceSelection(); return Task.CompletedTask; }, ReportCommandError);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportCommandError);
        PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CanPreviousPage);
        NextPageCommand = new RelayCommand(() => CurrentPage++, () => CanNextPage);
        showAllDraftChannelsCommand = new RelayCommand(
            () => draft.SetAllEegChannelsDisplayed(true),
            () => CanEditSignalDraft && DraftRows.Any(row => row.IsEegInput && !row.IsEnabled));
        hideAllDraftChannelsCommand = new RelayCommand(
            () => draft.SetAllEegChannelsDisplayed(false),
            () => CanEditSignalDraft && DraftRows.Any(row => row.IsEegInput && row.IsEnabled));
        this.mapping.DisplayChannelsChanged += OnMappingChanged;
        catalogue.PropertyChanged += OnCataloguePropertyChanged;
        catalogue.FilteredProfilesUpdated += OnFilteredProfilesUpdated;
        draft.PropertyChanged += OnDraftPropertyChanged;
    }

    public event EventHandler? ProfilesChanged;
    public ObservableCollection<ChannelConfigurationProfile> Profiles => catalogue.Profiles;
    public ObservableCollection<ChannelConfigurationProfile> FilteredProfiles => catalogue.FilteredProfiles;
    public IReadOnlyList<string> SourceFilterOptions => catalogue.SourceFilterOptions;
    public IReadOnlyList<string> StatusFilterOptions => catalogue.StatusFilterOptions;
    public IReadOnlyList<int> PageSizeOptions => catalogue.PageSizeOptions;
    public int PageSize { get => catalogue.PageSize; set => catalogue.PageSize = value; }
    public int CurrentPage { get => catalogue.CurrentPage; set => catalogue.CurrentPage = value; }
    public int TotalItemCount => catalogue.TotalItemCount;
    public int TotalPages => catalogue.TotalPages;
    public bool CanPreviousPage => catalogue.CanPreviousPage;
    public bool CanNextPage => catalogue.CanNextPage;
    public IReadOnlyList<string> DeviceModelFilterOptions => catalogue.DeviceModelFilterOptions;
    public string SearchText { get => catalogue.SearchText; set => catalogue.SearchText = value; }
    public string DeviceModelFilter { get => catalogue.DeviceModelFilter; set => catalogue.DeviceModelFilter = value; }
    public string SourceFilter { get => catalogue.SourceFilter; set => catalogue.SourceFilter = value; }
    public string StatusFilter { get => catalogue.StatusFilter; set => catalogue.StatusFilter = value; }

    public ICommand RefreshCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand ResolveElectrodeLocationsCommand { get; }
    public ICommand CloseDraftCommand { get; }
    public ICommand ConfirmDeviceSelectionCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ShowAllDraftChannelsCommand => showAllDraftChannelsCommand;
    public ICommand HideAllDraftChannelsCommand => hideAllDraftChannelsCommand;

    public ChannelConfigurationProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                RaisePropertyChanged(nameof(CanDeleteSelected));
                RaisePropertyChanged(nameof(CanCreateVersionSelected));
                RaisePageCommandStateChanged();
            }
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool CanCreateProfile => mapping.HasLoadedDevice;
    public string? CurrentDeviceSignature => mapping.HasLoadedDevice ? mapping.GetDeviceSignature() : null;
    public bool CanDeleteSelected => SelectedProfile?.CanDelete == true;
    public bool CanCreateVersionSelected => SelectedProfile?.CanCreateVersion == true;

    public ObservableCollection<ChannelConfigurationDraftRow> DraftRows => draft.DraftRows;
    public ElectrodeMapLandmarkDraft DraftHardwareLandmarks => draft.DraftHardwareLandmarks;
    public ObservableCollection<ChannelConfigurationDraftRow> FilteredDraftRows => draft.FilteredDraftRows;
    public string ChannelSearchText { get => draft.ChannelSearchText; set => draft.ChannelSearchText = value; }
    public string DraftSourceLabel => draft.DraftSourceLabel;
    public string DraftDeviceVendor => draft.DraftDeviceVendor;
    public string DraftDeviceModel => draft.DraftDeviceModel;
    public int DraftMontageReferenceCount => draft.DraftMontageReferenceCount;
    public string DraftCreatedAtText => draft.DraftCreatedAtText;
    public string DraftUpdatedAtText => draft.DraftUpdatedAtText;
    public string DraftStatusLabel => draft.DraftStatusLabel;
    public AcquisitionDeviceDescriptor? DraftDevice { get => draft.DraftDevice; set => draft.DraftDevice = value; }
    public AcquisitionDeviceDescriptor? PendingDeviceSelection { get => draft.PendingDeviceSelection; set => draft.PendingDeviceSelection = value; }
    public bool HasPendingDeviceSelection => draft.HasPendingDeviceSelection;
    public bool IsDraftOpen => draft.IsDraftOpen;
    public bool IsDevicePickerVisible => draft.IsDevicePickerVisible;
    public bool CanEditDraft => draft.CanEditDraft;
    public bool HasUnsavedChanges => draft.HasUnsavedChanges;
    public bool CanEditSignalDraft => draft.CanEditSignalDraft;
    public bool IsSignalLockedDraft => draft.IsSignalLockedDraft;
    public string DraftLockMessage => draft.DraftLockMessage;
    public string DraftTitle => draft.DraftTitle;
    public string DraftName { get => draft.DraftName; set => draft.DraftName = value; }
    public string DraftDescription { get => draft.DraftDescription; set => draft.DraftDescription = value; }
    public int DraftEegInputCount => draft.DraftEegInputCount;
    public int DraftEnabledCount => draft.DraftEnabledCount;
    public string DraftReferenceElectrodeLocation { get => draft.DraftReferenceElectrodeLocation; set => draft.DraftReferenceElectrodeLocation = value; }
    public string DraftGroundElectrodeLocation { get => draft.DraftGroundElectrodeLocation; set => draft.DraftGroundElectrodeLocation = value; }

    public void BeginNewProfile(bool showDevicePicker = true)
    {
        SelectedProfile = null;
        draft.BeginNew(showDevicePicker);
        StatusText = "请选择设备；软件会读取实际物理输入并在设备布局匹配时套用默认电极模板。";
    }

    public void BeginEditSelected()
    {
        if (SelectedProfile is { } profile) draft.BeginEdit(profile);
    }

    public void BeginNewVersionSelected()
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("请先选择一份通道配置。");
        if (!mapping.HasLoadedDevice || !string.Equals(profile.DeviceSignature, mapping.GetDeviceSignature(), StringComparison.Ordinal))
            throw new InvalidOperationException("请连接与该通道配置兼容的设备后再创建新版本。");
        draft.BeginNewVersion(profile);
        StatusText = $"已基于“{profile.DisplayName}”创建可编辑的新版本草稿；原配置及其导联引用不会改变。";
    }

    public void BeginCopySelected()
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("请先选择一份通道配置。");
        draft.BeginCopy(profile);
        StatusText = $"已基于“{profile.DisplayName}”创建副本草稿；保存后将成为独立的用户配置。";
    }

    public void CloseDraft() => draft.Close();

    public void ConfirmPendingDeviceSelection()
    {
        var selected = draft.ConfirmPendingDeviceSelection();
        StatusText = $"已读取设备“{selected.DisplayName}”的物理输入，并生成通道映射草稿。";
        notifications?.PublishSuccess(StatusText);
    }

    public void ResetDraftMapPositionsToDefaults() => draft.ResetMapPositionsToDefaults();
    public void CancelDeviceSelection() => draft.CancelDeviceSelection();

    public async Task RefreshAsync()
    {
        var generation = ++refreshGeneration;
        var selectedId = SelectedProfile?.Id;
        var referenceCounts = await referencePolicy.GetReferenceCountsAsync(CancellationToken.None);
        if (generation != refreshGeneration) return;

        var loadedProfiles = new List<ChannelConfigurationProfile>();
        string? systemLocationStatus = null;
        if (mapping.TryCreateDefaultSystemProfile(out var systemProfile))
        {
            var locationResolution = await AntSystemChannelLocationResolver.TryResolveAsync(
                systemProfile, electrodeLocationClient, CancellationToken.None);
            if (generation != refreshGeneration) return;
            systemProfile = locationResolution.Profile;
            systemLocationStatus = locationResolution.Warning;
            var systemReferenceCount = SystemMontageProfileFactory.Create(systemProfile).Count;
            loadedProfiles.Add(PrepareForCurrentDevice(
                systemProfile, checked(referenceCounts.GetValueOrDefault(systemProfile.Id) + systemReferenceCount)));
        }

        foreach (var profile in await store.LoadAsync(CancellationToken.None))
        {
            if (generation != refreshGeneration) return;
            loadedProfiles.Add(PrepareForCurrentDevice(profile, referenceCounts.GetValueOrDefault(profile.Id)));
        }

        catalogue.ReplaceProfiles(loadedProfiles);
        SelectedProfile = selectedId is null
            ? FilteredProfiles.FirstOrDefault()
            : FilteredProfiles.FirstOrDefault(profile => profile.Id == selectedId)
              ?? FilteredProfiles.FirstOrDefault();

        StatusText = mapping.HasLoadedDevice
            ? $"已载入 {Profiles.Count} 个通道配置。当前设备有 {mapping.ActualEegInputCount} 个可标注 EEG 输入。"
            : "请先连接放大器；系统只会向导联配置提供与当前设备兼容的通道配置。";
        if (systemLocationStatus is not null)
        {
            StatusText = AntSystemChannelLocationResolver.AppendWarning(StatusText, systemLocationStatus);
            notifications?.PublishWarning(systemLocationStatus);
        }
        RaisePropertyChanged(nameof(CanCreateProfile));
        RaisePropertyChanged(nameof(CanCreateVersionSelected));
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveDraftAsync()
    {
        if (!CanEditDraft) throw new InvalidOperationException("系统默认通道配置只能查看，不能修改。");
        var profile = draft.CreateProfileSnapshot(DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidOperationException("配置名称不能为空。");
        ChannelConfigurationValidation.Validate(profile);
        if (draft.DraftSourceProfile is { Source: ChannelConfigurationSource.User } original &&
            string.Equals(original.Id, profile.Id, StringComparison.Ordinal))
        {
            await referencePolicy.EnsureSignalChangeAllowedAsync(original, profile, CancellationToken.None);
        }
        await store.SaveAsync(profile, CancellationToken.None);
        await RefreshAsync();
        SelectedProfile = Profiles.FirstOrDefault(item =>
            item.Id == profile.Id && item.Source == ChannelConfigurationSource.User)
            ?? throw new InvalidOperationException("保存后未找到用户通道配置。");
        draft.MarkSavedBaseline(profile);
        StatusText = $"已保存通道配置“{profile.DisplayName}”。";
        notifications?.PublishSuccess($"已保存通道配置“{profile.DisplayName}”。请点击“应用”后用于采集。");
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is not { CanDelete: true } profile)
            throw new InvalidOperationException("系统默认或已被导联引用的通道配置不能删除。");
        await referencePolicy.EnsureDeleteAllowedAsync(profile, CancellationToken.None);
        await store.DeleteAsync(profile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshAsync();
        StatusText = $"已删除通道配置“{profile.DisplayName}”。";
        notifications?.PublishSuccess($"已删除通道配置“{profile.DisplayName}”。");
    }

    private async Task ResolveElectrodeLocationsAsync()
    {
        var sessionVersion = draft.SessionVersion;
        await locationResolver.ResolveAsync(DraftRows, CancellationToken.None);
        if (sessionVersion == draft.SessionVersion)
            StatusText = "已按 MNE colin27_1020 更新可识别通道的位置；不可识别标签保持不可用。";
    }

    private void OnCataloguePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) RaisePropertyChanged(e.PropertyName);
    }

    private void OnFilteredProfilesUpdated(object? sender, EventArgs e)
    {
        if (SelectedProfile is not null && !FilteredProfiles.Contains(SelectedProfile))
            SelectedProfile = FilteredProfiles.FirstOrDefault();
        RaisePageCommandStateChanged();
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) RaisePropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(DraftEegInputCount) or nameof(DraftEnabledCount) or nameof(CanEditSignalDraft))
        {
            showAllDraftChannelsCommand.RaiseCanExecuteChanged();
            hideAllDraftChannelsCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnMappingChanged(object? sender, EventArgs eventArgs)
    {
        RaisePropertyChanged(nameof(CanCreateProfile));
        var currentSignature = mapping.HasLoadedDevice ? mapping.GetDeviceSignature() : null;
        if (string.Equals(observedDeviceSignature, currentSignature, StringComparison.Ordinal)) return;
        observedDeviceSignature = currentSignature;
        _ = RefreshAsync();
    }

    private ChannelConfigurationProfile PrepareForCurrentDevice(ChannelConfigurationProfile profile, int montageReferenceCount)
    {
        if (!mapping.HasLoadedDevice)
        {
            return profile with
            {
                MontageReferenceCount = montageReferenceCount, IsCompatibleWithCurrentDevice = false,
                StatusLabel = "待连接", StatusDetail = "连接设备后验证此配置是否可用。",
            };
        }
        var matchesCurrentDevice = string.Equals(profile.DeviceSignature, mapping.GetDeviceSignature(), StringComparison.Ordinal);
        return profile with
        {
            IsActive = false, HasUnappliedChanges = false,
            MontageReferenceCount = montageReferenceCount, IsCompatibleWithCurrentDevice = matchesCurrentDevice,
            StatusLabel = matchesCurrentDevice ? "可用于导联" : "设备不匹配",
            StatusDetail = matchesCurrentDevice ? "配置有效且与当前设备兼容，可在导联配置中选择。" : "当前设备的物理输入或通道类型与此配置不匹配。",
        };
    }

    private void RaisePageCommandStateChanged()
    {
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ReportCommandError(Exception exception)
    {
        StatusText = exception.Message;
        notifications?.PublishError(exception.Message);
    }
}

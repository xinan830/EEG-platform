using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// Coordinates display-montage persistence and lifecycle by composing the
/// catalogue view and single-owner draft editor.
/// </summary>
public sealed partial class MontageConfigurationWorkspaceViewModel : ObservableObject
{
    private readonly ChannelConfigurationWorkspaceViewModel channelConfigurations;
    private readonly MontageProfileStore store;
    private readonly OperationNotificationCenter notifications;
    private readonly MontageProfileCatalogueView catalogue = new();
    private readonly MontageDraftEditor draft = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private MontageProfile? selectedProfile;
    private string statusText = "请选择一份通道配置后创建导联配置。";
    private IReadOnlySet<string> referencedIds = new HashSet<string>(StringComparer.Ordinal);
    private bool isRefreshingChannelConfigurations;

    public MontageConfigurationWorkspaceViewModel(
        ChannelConfigurationWorkspaceViewModel channelConfigurations,
        MontageProfileStore? store = null,
        OperationNotificationCenter? notifications = null)
    {
        this.channelConfigurations = channelConfigurations;
        this.store = store ?? new MontageProfileStore();
        this.notifications = notifications ?? new OperationNotificationCenter();
        this.channelConfigurations.ProfilesChanged += OnChannelConfigurationProfilesChanged;
        catalogue.PropertyChanged += OnChildPropertyChanged;
        catalogue.FilteredProfilesUpdated += OnFilteredProfilesUpdated;
        draft.PropertyChanged += OnChildPropertyChanged;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCommandError);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, ReportCommandError);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportCommandError);
        ArchiveSelectedCommand = new AsyncRelayCommand(ArchiveSelectedAsync, ReportCommandError);
        ApplyAverageReferenceToAllCommand = new AsyncRelayCommand(() => { ApplyAverageReferenceToAll(); return Task.CompletedTask; }, ReportCommandError);
        ApplyOriginalReferenceToAllCommand = new AsyncRelayCommand(() => { ApplyOriginalReferenceToAll(); return Task.CompletedTask; }, ReportCommandError);
        ApplySpecifiedPairToAllCommand = new AsyncRelayCommand(() => { ApplySpecifiedPairToAll(); return Task.CompletedTask; }, ReportCommandError);
        PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CanPreviousPage);
        NextPageCommand = new RelayCommand(() => CurrentPage++, () => CanNextPage);
    }

    public ObservableCollection<MontageProfile> Profiles => catalogue.Profiles;
    public ObservableCollection<MontageProfile> FilteredProfiles => catalogue.FilteredProfiles;
    public ObservableCollection<MontageProfile> AvailableForAcquisition => catalogue.AvailableForAcquisition;
    public ObservableCollection<ChannelConfigurationProfile> AvailableChannelConfigurations => draft.AvailableChannelConfigurations;
    public ObservableCollection<MontageDraftRow> DraftRows => draft.DraftRows;
    public MontageOutputOrder OutputOrder => draft.OutputOrder;
    public ObservableCollection<AverageReferenceChannelOption> AverageReferenceChannels => draft.AverageReferenceChannels;

    public IReadOnlyList<MontageNegativeKindOption> NegativeKinds { get; } =
    [
        new(MontageNegativeKind.OriginalHardwareReference, "原始硬件参考"),
        new(MontageNegativeKind.Channel, "指定通道"),
        new(MontageNegativeKind.Mean, "平均参考"),
        new(MontageNegativeKind.SpecifiedPair, "指定双参考"),
    ];

    public IReadOnlyList<string> SourceFilterOptions => catalogue.SourceFilterOptions;
    public IReadOnlyList<string> StatusFilterOptions => catalogue.StatusFilterOptions;
    public IReadOnlyList<string> MontageMethodFilterOptions => catalogue.MontageMethodFilterOptions;
    public IReadOnlyList<int> PageSizeOptions => catalogue.PageSizeOptions;
    public IReadOnlyList<string> ChannelConfigurationFilterOptions => catalogue.ChannelConfigurationFilterOptions;

    public ICommand RefreshCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ArchiveSelectedCommand { get; }
    public ICommand ApplyAverageReferenceToAllCommand { get; }
    public ICommand ApplyOriginalReferenceToAllCommand { get; }
    public ICommand ApplySpecifiedPairToAllCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }

    public string SearchText { get => catalogue.SearchText; set => catalogue.SearchText = value; }
    public string ChannelConfigurationFilter { get => catalogue.ChannelConfigurationFilter; set => catalogue.ChannelConfigurationFilter = value; }
    public string MontageMethodFilter { get => catalogue.MontageMethodFilter; set => catalogue.MontageMethodFilter = value; }
    public string SourceFilter { get => catalogue.SourceFilter; set => catalogue.SourceFilter = value; }
    public string StatusFilter { get => catalogue.StatusFilter; set => catalogue.StatusFilter = value; }
    public int PageSize { get => catalogue.PageSize; set => catalogue.PageSize = value; }
    public int CurrentPage { get => catalogue.CurrentPage; set => catalogue.CurrentPage = value; }
    public int TotalItemCount => catalogue.TotalItemCount;
    public int TotalPages => catalogue.TotalPages;
    public bool CanPreviousPage => catalogue.CanPreviousPage;
    public bool CanNextPage => catalogue.CanNextPage;

    public MontageProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                RaisePropertyChanged(nameof(CanDeleteSelected));
                RaisePropertyChanged(nameof(CanCopySelected));
                RaisePropertyChanged(nameof(CanArchiveSelected));
                RaisePageCommandStateChanged();
            }
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsEditingExisting => draft.IsEditingExisting;
    public bool CanEditDraft => draft.CanEditDraft;
    public string DraftPermissionText => draft.DraftPermissionText;
    public string DraftTitle => draft.DraftTitle;
    public bool ShowReferenceGroup => draft.ShowReferenceGroup;
    public bool CanSelectDraftChannelConfiguration => draft.CanSelectDraftChannelConfiguration;
    public bool CanDeleteSelected => SelectedProfile?.CanDelete == true;
    public bool CanCopySelected => SelectedProfile?.CanCopy == true;
    public bool CanArchiveSelected => SelectedProfile?.CanArchive == true;
    public string DraftName { get => draft.DraftName; set => draft.DraftName = value; }
    public string DraftDescription { get => draft.DraftDescription; set => draft.DraftDescription = value; }
    public ChannelConfigurationProfile? DraftChannelConfiguration { get => draft.DraftChannelConfiguration; set => draft.DraftChannelConfiguration = value; }
    public string? DraftChannelConfigurationId { get => draft.DraftChannelConfigurationId; set => draft.DraftChannelConfigurationId = value; }
    public string DraftChannelSummary => draft.DraftChannelSummary;
    public string AverageReferenceSummary => draft.AverageReferenceSummary;
    public string SpecifiedPairSummary => draft.SpecifiedPairSummary;
    public bool CanEditReferenceMode => draft.CanEditReferenceMode;
    public bool CanApplyOriginalReference => draft.CanApplyOriginalReference;
    public bool CanApplyAverageReference => draft.CanApplyAverageReference;
    public bool CanApplySpecifiedPair => draft.CanApplySpecifiedPair;

    public void BeginNewProfile()
    {
        SelectedProfile = null;
        StatusText = draft.BeginNew(channelConfigurations.Profiles);
    }

    public void BeginEditSelected()
    {
        if (SelectedProfile is null) return;
        StatusText = draft.BeginEdit(SelectedProfile);
    }

    public void BeginCopySelected()
    {
        if (SelectedProfile is null) throw new InvalidOperationException("请先选择一份导联配置。");
        StatusText = draft.BeginCopy(SelectedProfile);
    }

    public void CloseDraft() => draft.Close();

    public async Task RefreshAsync()
    {
        isRefreshingChannelConfigurations = true;
        try { await channelConfigurations.RefreshAsync(); }
        finally { isRefreshingChannelConfigurations = false; }
        await ReloadProfilesAsync();
    }

    public async Task SaveDraftAsync()
    {
        var profile = draft.BuildValidatedProfile(SelectedProfile?.Revision);
        if (IsSessionDraft)
        {
            SessionProfileSaved?.Invoke(this, profile);
            return;
        }

        await store.SaveAsync(profile, CancellationToken.None);
        await RefreshAsync();
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == profile.Id)
            ?? throw new InvalidOperationException("保存后未找到导联配置。");
        StatusText = $"已保存导联配置“{profile.DisplayName}”。";
        notifications.PublishSuccess($"已保存导联配置“{profile.DisplayName}”。");
    }

    /// <summary>Applies the user-selected shared average group to every fixed source row.</summary>
    public void ApplyAverageReferenceToAll() { StatusText = draft.ApplyAverageReferenceToAll(); notifications.PublishSuccess(StatusText); }

    public void ApplyOriginalReferenceToAll() { StatusText = draft.ApplyOriginalReferenceToAll(); notifications.PublishSuccess(StatusText); }

    public void ApplySpecifiedPairToAll() { StatusText = draft.ApplySpecifiedPairToAll(); notifications.PublishSuccess(StatusText); }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is not { Source: MontageProfileSource.User } profile)
        {
            throw new InvalidOperationException("请选择用户创建的导联配置；系统预设不能删除。");
        }

        await store.DeleteAsync(profile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshAsync();
        StatusText = $"已删除导联配置“{profile.DisplayName}”。";
        notifications.PublishSuccess($"已删除导联配置“{profile.DisplayName}”。");
    }

    public Task RefreshReferencesAsync() => ReloadProfilesAsync();

    public Task RegisterRecordingReferenceAsync(IReadOnlyDictionary<string, string> metadata, string recordingDirectory) =>
        store.RegisterRecordingReferenceAsync(metadata, CancellationToken.None, recordingDirectory: recordingDirectory);

    public async Task ArchiveSelectedAsync()
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("请先选择导联配置。");
        await store.ArchiveAsync(profile.Id, CancellationToken.None);
        await ReloadProfilesAsync();
        StatusText = $"已归档导联配置“{profile.DisplayName}”，历史快照保持不变。";
        notifications.PublishSuccess(StatusText);
    }

    internal static IReadOnlyList<MontageProfile> DeduplicateProfilesByStableIdentity(IEnumerable<MontageProfile> profiles) =>
        MontageProfileCatalogue.Deduplicate(profiles);

    private async void OnChannelConfigurationProfilesChanged(object? sender, EventArgs eventArgs)
    {
        if (isRefreshingChannelConfigurations) return;
        try { await ReloadProfilesAsync(); }
        catch (Exception exception) { ReportCommandError(exception); }
    }

    private async Task ReloadProfilesAsync()
    {
        await refreshGate.WaitAsync();
        try
        {
            var selectedId = SelectedProfile?.Id;
            referencedIds = await store.GetReferencedIdsAsync(CancellationToken.None);
            draft.RefreshAvailableChannelConfigurations(channelConfigurations.Profiles);
            var persistedProfiles = await store.LoadAsync(CancellationToken.None);
            var prepared = DeduplicateProfilesByStableIdentity(CreateSystemProfiles().Concat(persistedProfiles))
                .Select(PrepareSourceStatus)
                .ToArray();

            catalogue.ReplaceProfiles(prepared);
            SelectedProfile = selectedId is null
                ? FilteredProfiles.FirstOrDefault()
                : FilteredProfiles.FirstOrDefault(profile => profile.Id == selectedId)
                  ?? FilteredProfiles.FirstOrDefault();
            draft.SyncReadOnlyStateFromReloadedProfiles(Profiles);
            StatusText = $"已载入 {Profiles.Count} 份导联配置；可用通道配置 {AvailableChannelConfigurations.Count} 份。";
            RaisePropertyChanged(nameof(AvailableForAcquisition));
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private MontageProfile PrepareSourceStatus(MontageProfile profile) => MontageProfileAvailability.Evaluate(
        profile with { IsRecordingReferenced = referencedIds.Contains(profile.Id) },
        channelConfigurations.Profiles, channelConfigurations.CurrentDeviceSignature);

    private IEnumerable<MontageProfile> CreateSystemProfiles() =>
        AvailableChannelConfigurations.SelectMany(SystemMontageProfileFactory.Create);

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) RaisePropertyChanged(e.PropertyName);
    }

    private void OnFilteredProfilesUpdated(object? sender, EventArgs e)
    {
        if (SelectedProfile is not null && !FilteredProfiles.Contains(SelectedProfile))
        {
            SelectedProfile = FilteredProfiles.FirstOrDefault();
        }

        RaisePageCommandStateChanged();
    }

    private void RaisePageCommandStateChanged()
    {
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ReportCommandError(Exception exception)
    {
        StatusText = exception.Message;
        notifications.PublishError(exception.Message);
    }
}

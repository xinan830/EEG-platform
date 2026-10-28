using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// Coordinates display-montage profiles. Channel configuration owns physical
/// source channels; this workspace only selects a controlled rereference rule.
/// </summary>
public sealed class MontageConfigurationWorkspaceViewModel : ObservableObject
{
    private readonly ChannelConfigurationWorkspaceViewModel channelConfigurations;
    private readonly MontageProfileStore store;
    private readonly OperationNotificationCenter notifications;
    private MontageProfile? selectedProfile;
    private string statusText = "请选择一份通道配置后创建导联配置。";
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private ChannelConfigurationProfile? draftChannelConfiguration;
    private string? draftProfileId;
    private DateTimeOffset? draftCreatedAtUtc;
    private bool isReadOnlyDraft;
    private string draftPermissionText = "可编辑";
    private IReadOnlySet<string> referencedIds = new HashSet<string>(StringComparer.Ordinal);
    private bool hasSparseOutputTopology;
    private bool isRefreshingChannelConfigurations;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private string searchText = string.Empty;
    private string channelConfigurationFilter = "全部通道配置";
    private string montageMethodFilter = "全部方式";
    private string sourceFilter = "全部来源";
    private string statusFilter = "全部状态";
    private int pageSize = 10;
    private int currentPage = 1;

    public MontageConfigurationWorkspaceViewModel(
        ChannelConfigurationWorkspaceViewModel channelConfigurations,
        MontageProfileStore? store = null,
        OperationNotificationCenter? notifications = null)
    {
        this.channelConfigurations = channelConfigurations;
        this.store = store ?? new MontageProfileStore();
        this.notifications = notifications ?? new OperationNotificationCenter();
        OutputOrder = new MontageOutputOrder(DraftRows, () => CanEditDraft);
        this.channelConfigurations.ProfilesChanged += OnChannelConfigurationProfilesChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCommandError);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, ReportCommandError);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportCommandError);
        ArchiveSelectedCommand = new AsyncRelayCommand(ArchiveSelectedAsync, ReportCommandError);
        ApplyAverageReferenceToAllCommand = new AsyncRelayCommand(
            () => { ApplyAverageReferenceToAll(); return Task.CompletedTask; },
            ReportCommandError);
        ApplyOriginalReferenceToAllCommand = new AsyncRelayCommand(
            () => { ApplyOriginalReferenceToAll(); return Task.CompletedTask; },
            ReportCommandError);
        ApplySpecifiedPairToAllCommand = new AsyncRelayCommand(
            () => { ApplySpecifiedPairToAll(); return Task.CompletedTask; },
            ReportCommandError);
        PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CanPreviousPage);
        NextPageCommand = new RelayCommand(() => CurrentPage++, () => CanNextPage);
    }

    public ObservableCollection<MontageProfile> Profiles { get; } = [];

    /// <summary>Profiles after the list-page search, filter, and paging pipeline.</summary>
    public ObservableCollection<MontageProfile> FilteredProfiles { get; } = [];

    public ObservableCollection<MontageProfile> AvailableForAcquisition { get; } = [];

    public ObservableCollection<ChannelConfigurationProfile> AvailableChannelConfigurations { get; } = [];

    public ObservableCollection<MontageDraftRow> DraftRows { get; } = [];

    public MontageOutputOrder OutputOrder { get; }

    public ObservableCollection<AverageReferenceChannelOption> AverageReferenceChannels { get; } = [];

    public IReadOnlyList<MontageNegativeKindOption> NegativeKinds { get; } =
    [
        new(MontageNegativeKind.OriginalHardwareReference, "原始硬件参考"),
        new(MontageNegativeKind.Channel, "指定通道"),
        new(MontageNegativeKind.Mean, "平均参考"),
        new(MontageNegativeKind.SpecifiedPair, "指定双参考"),
    ];

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["全部来源", "系统默认", "用户创建"];

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        ["全部状态", "可用", "被引用", "已归档", "设备不匹配", "来源已修改", "来源已删除", "配置无效"];

    public IReadOnlyList<string> MontageMethodFilterOptions { get; } =
        ["全部方式", "原始硬件参考", "指定通道", "平均参考", "指定双参考"];

    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 20, 50];

    public IReadOnlyList<string> ChannelConfigurationFilterOptions =>
        ["全部通道配置", .. Profiles
            .Select(profile => profile.ChannelConfigurationName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    public ICommand RefreshCommand { get; }

    public ICommand SaveDraftCommand { get; }

    public ICommand DeleteSelectedCommand { get; }

    public ICommand ArchiveSelectedCommand { get; }

    public ICommand ApplyAverageReferenceToAllCommand { get; }

    public ICommand ApplyOriginalReferenceToAllCommand { get; }

    public ICommand ApplySpecifiedPairToAllCommand { get; }

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string ChannelConfigurationFilter
    {
        get => channelConfigurationFilter;
        set { if (SetProperty(ref channelConfigurationFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string MontageMethodFilter
    {
        get => montageMethodFilter;
        set { if (SetProperty(ref montageMethodFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string SourceFilter
    {
        get => sourceFilter;
        set { if (SetProperty(ref sourceFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string StatusFilter
    {
        get => statusFilter;
        set { if (SetProperty(ref statusFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public int PageSize
    {
        get => pageSize;
        set
        {
            var normalized = PageSizeOptions.Contains(value) ? value : 10;
            if (SetProperty(ref pageSize, normalized))
            {
                currentPage = 1;
                RaisePropertyChanged(nameof(CurrentPage));
                ApplyFilters();
            }
        }
    }

    public int CurrentPage
    {
        get => currentPage;
        set
        {
            var normalized = Math.Clamp(value, 1, TotalPages);
            if (SetProperty(ref currentPage, normalized))
            {
                ApplyFilters();
            }
        }
    }

    public int TotalItemCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public bool CanPreviousPage => CurrentPage > 1;

    public bool CanNextPage => CurrentPage < TotalPages;

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

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

    public bool IsEditingExisting => draftProfileId is not null;

    public bool CanEditDraft => !isReadOnlyDraft;

    public string DraftPermissionText => draftPermissionText;

    public string DraftTitle => CanEditDraft
        ? IsEditingExisting ? "编辑导联配置" : "新建导联配置"
        : "查看导联配置";

    public bool ShowReferenceGroup => CanEditDraft || DraftRows.Any(row =>
        row.NegativeKind is MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair);

    public bool CanSelectDraftChannelConfiguration => CanEditDraft && !IsEditingExisting;

    public bool CanDeleteSelected => SelectedProfile?.CanDelete == true;

    public bool CanCopySelected => SelectedProfile?.CanCopy == true;

    public bool CanArchiveSelected => SelectedProfile?.CanArchive == true;

    public string DraftName { get => draftName; set => SetProperty(ref draftName, value); }

    public string DraftDescription { get => draftDescription; set => SetProperty(ref draftDescription, value); }

    public ChannelConfigurationProfile? DraftChannelConfiguration
    {
        get => draftChannelConfiguration;
        set
        {
            if (SetProperty(ref draftChannelConfiguration, value))
            {
                RaisePropertyChanged(nameof(DraftChannelConfigurationId));
                RaisePropertyChanged(nameof(DraftChannelSummary));
                if (!IsEditingExisting && value is not null)
                {
                    BuildFixedRows(value, null);
                }
            }
        }
    }

    /// <summary>
    /// The detail ComboBox must select by stable identity rather than by the
    /// snapshot object instance embedded in a saved montage profile.
    /// </summary>
    public string? DraftChannelConfigurationId
    {
        get => DraftChannelConfiguration?.Id;
        set
        {
            if (string.Equals(value, DraftChannelConfiguration?.Id, StringComparison.Ordinal))
            {
                return;
            }

            var selected = AvailableChannelConfigurations.FirstOrDefault(profile =>
                string.Equals(profile.Id, value, StringComparison.Ordinal));
            if (selected is not null)
            {
                DraftChannelConfiguration = selected;
            }
        }
    }

    public string DraftChannelSummary => DraftChannelConfiguration is null
        ? "未选择"
        : $"{DraftChannelConfiguration.Name} · {DraftChannelConfiguration.EnabledChannelCount} 个固定源通道";

    public string AverageReferenceSummary => $"AVG（{AverageReferenceChannels.Count(option => option.IsIncluded)} 通道）";

    public string SpecifiedPairSummary => GetAverageReferenceLabels().Count == 2
        ? $"双参考（{string.Join("、", GetAverageReferenceLabels())}）"
        : "请选择两个通道";

    public bool CanEditReferenceMode => CanEditDraft && !hasSparseOutputTopology;

    public bool CanApplyOriginalReference => CanEditReferenceMode && DraftRows.Count > 0;

    public bool CanApplyAverageReference => CanEditReferenceMode && GetAverageReferenceLabels().Count >= 2 && DraftRows.Count > 0;

    public bool CanApplySpecifiedPair => CanEditReferenceMode && GetAverageReferenceLabels().Count == 2 && DraftRows.Count > 0;

    public void BeginNewProfile()
    {
        isReadOnlyDraft = false;
        draftPermissionText = "可编辑";
        hasSparseOutputTopology = false;
        RefreshAvailableChannelConfigurations();
        SelectedProfile = null;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftChannelConfiguration = null;
        DraftRows.Clear();
        AverageReferenceChannels.Clear();
        DraftChannelConfiguration = FindPreferredChannelConfiguration();
        StatusText = DraftChannelConfiguration is null
            ? "请先保存一份有效、与当前设备兼容且含显示通道的通道配置。"
            : "每个源通道均固定生成一行；请选择其显示重参考方式。";
        RaiseDraftModePropertiesChanged();
    }

    public void BeginEditSelected()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var profile = SelectedProfile;
        isReadOnlyDraft = !profile.CanEdit;
        draftPermissionText = profile.EditPermissionText;
        hasSparseOutputTopology = !HasOneOutputPerConfiguredSource(profile);
        draftProfileId = profile.Source == MontageProfileSource.User ? profile.Id : null;
        draftCreatedAtUtc = profile.Source == MontageProfileSource.User ? profile.CreatedAtUtc : null;
        DraftName = profile.Name;
        DraftDescription = profile.Description;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        EnsureDraftChannelConfigurationOption(profile.ChannelConfigurationSnapshot);
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelConfigurationId));
        RaisePropertyChanged(nameof(DraftChannelSummary));
        if (isReadOnlyDraft)
        {
            BuildRowsFromExistingProfile(profile);
            StatusText = $"{DraftPermissionText}；如需修改，请复制为新配置。";
        }
        else
        {
            BuildEditableRows(profile);
            StatusText = "源通道由通道配置固定；可编辑参考规则、颜色和显示顺序。";
        }
        RaiseDraftModePropertiesChanged();
    }

    public void BeginCopySelected()
    {
        if (SelectedProfile is null)
        {
            throw new InvalidOperationException("请先选择一份导联配置。");
        }

        var profile = SelectedProfile;
        isReadOnlyDraft = false;
        draftPermissionText = "可编辑";
        hasSparseOutputTopology = !HasOneOutputPerConfiguredSource(profile);
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = $"{profile.Name} 副本";
        DraftDescription = profile.Description;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        EnsureDraftChannelConfigurationOption(profile.ChannelConfigurationSnapshot);
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelConfigurationId));
        RaisePropertyChanged(nameof(DraftChannelSummary));
        BuildEditableRows(profile);
        StatusText = $"已从“{profile.Name}”创建副本。";
        RaiseDraftModePropertiesChanged();
    }

    public void CloseDraft()
    {
        isReadOnlyDraft = false;
        hasSparseOutputTopology = false;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftRows.Clear();
        AverageReferenceChannels.Clear();
        DraftChannelConfiguration = null;
        RaiseDraftModePropertiesChanged();
    }

    public async Task RefreshAsync()
    {
        isRefreshingChannelConfigurations = true;
        try
        {
            await channelConfigurations.RefreshAsync();
        }
        finally
        {
            isRefreshingChannelConfigurations = false;
        }

        await ReloadProfilesAsync();
    }

    public async Task SaveDraftAsync()
    {
        if (!CanEditDraft)
        {
            throw new InvalidOperationException("此导联配置只读，不能修改；请复制为新配置。");
        }
        var snapshot = DraftChannelConfiguration ?? throw new InvalidOperationException("请选择适用的通道配置。");
        var name = DraftName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("导联配置名称不能为空。");
        }

        var averageLabels = GetAverageReferenceLabels();
        var channels = DraftRows.Select(row => row.ToModel(averageLabels)).ToArray();
        MontageValidation.Validate(snapshot, channels, averageLabels);
        var channelFingerprint = ChannelConfigurationFingerprint.Create(snapshot);
        var now = DateTimeOffset.UtcNow;
        var profile = new MontageProfile(
            draftProfileId ?? Guid.NewGuid().ToString("N"),
            name,
            DraftDescription.Trim(),
            MontageProfileSource.User,
            snapshot.DeviceSignature,
            channelFingerprint,
            snapshot,
            channels,
            draftProfileId is null ? 1 : (SelectedProfile?.Revision ?? 0) + 1,
            draftCreatedAtUtc ?? now,
            now,
            MontageProfileFingerprint.Create(channelFingerprint, channels, averageLabels),
            averageLabels);
        await store.SaveAsync(profile, CancellationToken.None);
        await RefreshAsync();
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == profile.Id)
            ?? throw new InvalidOperationException("保存后未找到导联配置。");
        StatusText = $"已保存导联配置“{profile.Name}”。";
        notifications.PublishSuccess($"已保存导联配置“{profile.Name}”。");
    }

    /// <summary>Applies the user-selected shared average group to every fixed source row.</summary>
    public void ApplyAverageReferenceToAll()
    {
        if (!CanApplyAverageReference)
        {
            throw new InvalidOperationException("请先在指定平均参考组中选择至少两个有效通道。");
        }
        foreach (var row in DraftRows)
        {
            row.NegativeKind = MontageNegativeKind.Mean;
        }
        StatusText = $"已将 {AverageReferenceSummary} 应用到全部 {DraftRows.Count} 个固定源通道。";
        notifications.PublishSuccess(StatusText);
    }

    public void ApplyOriginalReferenceToAll()
    {
        if (!CanApplyOriginalReference)
        {
            throw new InvalidOperationException("此导联配置的输出集合固定，不能整体切换参考方式。");
        }
        foreach (var row in DraftRows)
        {
            row.NegativeKind = MontageNegativeKind.OriginalHardwareReference;
        }
        StatusText = $"已恢复全部 {DraftRows.Count} 个通道的原始硬件参考显示。";
        notifications.PublishSuccess(StatusText);
    }

    public void ApplySpecifiedPairToAll()
    {
        if (!CanApplySpecifiedPair)
        {
            throw new InvalidOperationException("指定双参考必须选择恰好两个有效通道。");
        }
        foreach (var row in DraftRows)
        {
            var pair = GetAverageReferenceLabels();
            row.SetSpecifiedPair(pair[0], pair[1]);
            row.NegativeKind = MontageNegativeKind.SpecifiedPair;
        }
        StatusText = $"已将指定双参考（{string.Join("、", GetAverageReferenceLabels())}）应用到全部 {DraftRows.Count} 个固定源通道。";
        notifications.PublishSuccess(StatusText);
    }

    private void BuildFixedRows(ChannelConfigurationProfile source, MontageProfile? existing)
    {
        var fixedChannels = source.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => (channel.ElectrodeLabel.Trim(), channel.DisplayOrder))
            .ToArray();
        var averageLabels = existing?.AverageReferenceLabels is { Count: > 0 } savedAverageLabels
            ? savedAverageLabels
            : fixedChannels.Select(channel => channel.Item1).ToArray();
        BuildAverageReferenceOptions(fixedChannels.Select(channel => channel.Item1), averageLabels);
        var outputs = existing?.DerivedChannels ?? fixedChannels.Select(channel => new DerivedMontageChannel(
            MontageDisplay.CreateName(channel.Item1, MontageNegativeKind.OriginalHardwareReference, null),
            channel.Item1, MontageNegativeKind.OriginalHardwareReference, [], channel.DisplayOrder)).ToArray();
        ReplaceDraftRows(source, outputs, averageLabels);
    }

    /// <summary>
    /// Keeps an existing sparse output topology intact. Rebuilding a Cz or
    /// bipolar montage from every configured source would silently append
    /// unintended original-reference outputs.
    /// </summary>
    private void BuildEditableRows(MontageProfile profile)
    {
        if (HasOneOutputPerConfiguredSource(profile))
        {
            BuildFixedRows(profile.ChannelConfigurationSnapshot, profile);
            return;
        }

        BuildRowsFromExistingProfile(profile);
    }

    private static bool HasOneOutputPerConfiguredSource(MontageProfile profile)
    {
        var sourceLabels = profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positiveLabels = profile.DerivedChannels
            .Select(channel => channel.PositiveLabel.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return profile.DerivedChannels.Count == sourceLabels.Count && sourceLabels.SetEquals(positiveLabels);
    }

    private void BuildRowsFromExistingProfile(MontageProfile profile)
    {
        var sourceLabels = profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToArray();
        var averageLabels = profile.AverageReferenceLabels ?? [];
        BuildAverageReferenceOptions(sourceLabels, averageLabels);
        ReplaceDraftRows(profile.ChannelConfigurationSnapshot, profile.DerivedChannels, averageLabels);
    }

    private void ReplaceDraftRows(ChannelConfigurationProfile source,
        IReadOnlyList<DerivedMontageChannel> outputs, IReadOnlyList<string> averageLabels)
    {
        DraftRows.Clear();
        foreach (var row in MontageDraftRowFactory.Create(source, outputs, averageLabels,
                     AverageReferenceSummary, SpecifiedPairSummary)) DraftRows.Add(row);
    }

    private void BuildAverageReferenceOptions(IEnumerable<string> sourceLabels, IReadOnlyList<string> selectedLabels)
    {
        AverageReferenceChannels.Clear();
        var selected = selectedLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var label in sourceLabels)
        {
            var option = new AverageReferenceChannelOption(label, selected.Contains(label));
            option.PropertyChanged += OnAverageReferenceOptionChanged;
            AverageReferenceChannels.Add(option);
        }
        UpdateAverageReferenceSummary();
    }

    private void OnAverageReferenceOptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AverageReferenceChannelOption.IsIncluded))
        {
            UpdateAverageReferenceSummary();
        }
    }

    private void UpdateAverageReferenceSummary()
    {
        RaisePropertyChanged(nameof(AverageReferenceSummary));
        RaisePropertyChanged(nameof(SpecifiedPairSummary));
        RaisePropertyChanged(nameof(CanApplyOriginalReference));
        RaisePropertyChanged(nameof(CanApplyAverageReference));
        RaisePropertyChanged(nameof(CanApplySpecifiedPair));
        foreach (var row in DraftRows)
        {
            row.UpdateReferenceSummaries(AverageReferenceSummary, SpecifiedPairSummary, GetAverageReferenceLabels());
        }
    }

    private IReadOnlyList<string> GetAverageReferenceLabels() => AverageReferenceChannels
        .Where(option => option.IsIncluded)
        .Select(option => option.Label)
        .ToArray();

    private void RefreshAvailableChannelConfigurations()
    {
        AvailableChannelConfigurations.Clear();
        foreach (var profile in channelConfigurations.Profiles.Where(profile => profile.EnabledChannelCount > 0))
        {
            AvailableChannelConfigurations.Add(profile);
        }
    }

    private void EnsureDraftChannelConfigurationOption(ChannelConfigurationProfile snapshot)
    {
        if (AvailableChannelConfigurations.Any(profile =>
                string.Equals(profile.Id, snapshot.Id, StringComparison.Ordinal)))
        {
            return;
        }

        // A deleted source configuration is still retained in the montage
        // snapshot. Keep it visible in the read-only detail view instead of
        // rendering an empty ComboBox.
        AvailableChannelConfigurations.Add(snapshot);
    }

    private MontageProfile PrepareSourceStatus(MontageProfile profile) => MontageProfileAvailability.Evaluate(
        profile with { IsRecordingReferenced = referencedIds.Contains(profile.Id) },
        channelConfigurations.Profiles, channelConfigurations.CurrentDeviceSignature);

    private IEnumerable<MontageProfile> CreateSystemProfiles()
    {
        foreach (var channelConfiguration in AvailableChannelConfigurations)
        {
            foreach (var profile in SystemMontageProfileFactory.Create(channelConfiguration))
            {
                yield return profile;
            }
        }
    }

    /// <summary>
    /// Keeps one catalogue entry for each stable profile identity. System
    /// presets have fixed IDs, so legacy or repeated source entries must not
    /// produce multiple identical options in acquisition selection.
    /// </summary>
    internal static IReadOnlyList<MontageProfile> DeduplicateProfilesByStableIdentity(
        IEnumerable<MontageProfile> profiles)
        => MontageProfileCatalogue.Deduplicate(profiles);

    private ChannelConfigurationProfile? FindPreferredChannelConfiguration() =>
        AvailableChannelConfigurations.FirstOrDefault();

    private async void OnChannelConfigurationProfilesChanged(object? sender, EventArgs eventArgs)
    {
        if (isRefreshingChannelConfigurations)
        {
            return;
        }

        try
        {
            await ReloadProfilesAsync();
        }
        catch (Exception exception)
        {
            ReportCommandError(exception);
        }
    }

    private async Task ReloadProfilesAsync()
    {
        await refreshGate.WaitAsync();
        try
        {
            var selectedId = SelectedProfile?.Id;
            referencedIds = await store.GetReferencedIdsAsync(CancellationToken.None);
            RefreshAvailableChannelConfigurations();
            Profiles.Clear();
            var persistedProfiles = await store.LoadAsync(CancellationToken.None);
            foreach (var profile in DeduplicateProfilesByStableIdentity(
                         CreateSystemProfiles().Concat(persistedProfiles)))
            {
                Profiles.Add(PrepareSourceStatus(profile));
            }
            AvailableForAcquisition.Clear();
            foreach (var profile in Profiles.Where(profile => profile.ChannelSnapshotStatusLabel == "已同步" && !profile.IsArchived))
            {
                AvailableForAcquisition.Add(profile);
            }
            SelectedProfile = selectedId is null
                ? Profiles.FirstOrDefault()
                : Profiles.FirstOrDefault(profile => profile.Id == selectedId);
            var draftProfile = Profiles.FirstOrDefault(profile => profile.Id == draftProfileId);
            if (draftProfile is { CanEdit: false })
            {
                isReadOnlyDraft = true;
                draftPermissionText = draftProfile.EditPermissionText;
                RaiseDraftModePropertiesChanged();
            }
            RaisePropertyChanged(nameof(ChannelConfigurationFilterOptions));
            if (!ChannelConfigurationFilterOptions.Contains(ChannelConfigurationFilter, StringComparer.OrdinalIgnoreCase))
            {
                channelConfigurationFilter = "全部通道配置";
                RaisePropertyChanged(nameof(ChannelConfigurationFilter));
            }
            ApplyFilters();
            StatusText = $"已载入 {Profiles.Count} 份导联配置；可用通道配置 {AvailableChannelConfigurations.Count} 份。";
            RaisePropertyChanged(nameof(AvailableForAcquisition));
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is not { Source: MontageProfileSource.User } profile)
        {
            throw new InvalidOperationException("请选择用户创建的导联配置；系统预设不能删除。");
        }
        await store.DeleteAsync(profile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshAsync();
        StatusText = $"已删除导联配置“{profile.Name}”。";
        notifications.PublishSuccess($"已删除导联配置“{profile.Name}”。");
    }

    public Task RefreshReferencesAsync() => ReloadProfilesAsync();

    public Task RegisterRecordingReferenceAsync(IReadOnlyDictionary<string, string> metadata, string recordingDirectory) =>
        store.RegisterRecordingReferenceAsync(metadata, CancellationToken.None, recordingDirectory: recordingDirectory);

    public async Task ArchiveSelectedAsync()
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("请先选择导联配置。");
        await store.ArchiveAsync(profile.Id, CancellationToken.None);
        await ReloadProfilesAsync();
        StatusText = $"已归档导联配置“{profile.Name}”，历史快照保持不变。";
        notifications.PublishSuccess(StatusText);
    }

    private void ApplyFilters(bool resetToFirstPage = false)
    {
        if (resetToFirstPage && currentPage != 1)
        {
            currentPage = 1;
            RaisePropertyChanged(nameof(CurrentPage));
        }

        var search = SearchText.Trim();
        var filtered = Profiles.Where(profile =>
            string.IsNullOrWhiteSpace(search) ||
            profile.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            profile.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            profile.ChannelConfigurationName.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        if (ChannelConfigurationFilter != "全部通道配置")
        {
            filtered = filtered.Where(profile =>
                string.Equals(profile.ChannelConfigurationName, ChannelConfigurationFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (MontageMethodFilter != "全部方式")
        {
            filtered = filtered.Where(profile => profile.DerivedChannels.Any(channel =>
                string.Equals(MontageDisplay.TextFor(channel.NegativeKind), MontageMethodFilter, StringComparison.Ordinal)));
        }

        if (SourceFilter is "系统默认" or "用户创建")
        {
            var source = SourceFilter == "系统默认" ? MontageProfileSource.System : MontageProfileSource.User;
            filtered = filtered.Where(profile => profile.Source == source);
        }

        if (StatusFilter != "全部状态")
        {
            filtered = filtered.Where(profile => profile.ListStatusLabel == StatusFilter);
        }

        var results = filtered.ToArray();
        TotalItemCount = results.Length;
        TotalPages = Math.Max(1, (int)Math.Ceiling(results.Length / (double)PageSize));
        if (currentPage > TotalPages)
        {
            currentPage = TotalPages;
            RaisePropertyChanged(nameof(CurrentPage));
        }

        FilteredProfiles.Clear();
        foreach (var profile in results.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            FilteredProfiles.Add(profile);
        }

        if (SelectedProfile is not null && !FilteredProfiles.Contains(SelectedProfile))
        {
            SelectedProfile = FilteredProfiles.FirstOrDefault();
        }

        RaisePropertyChanged(nameof(TotalItemCount));
        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(CanPreviousPage));
        RaisePropertyChanged(nameof(CanNextPage));
        RaisePageCommandStateChanged();
    }

    private void RaisePageCommandStateChanged()
    {
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseDraftModePropertiesChanged()
    {
        OutputOrder.Refresh();
        RaisePropertyChanged(nameof(IsEditingExisting));
        RaisePropertyChanged(nameof(CanEditDraft));
        RaisePropertyChanged(nameof(DraftPermissionText));
        RaisePropertyChanged(nameof(CanEditReferenceMode));
        RaisePropertyChanged(nameof(CanApplyOriginalReference));
        RaisePropertyChanged(nameof(CanApplyAverageReference));
        RaisePropertyChanged(nameof(CanApplySpecifiedPair));
        RaisePropertyChanged(nameof(DraftTitle));
        RaisePropertyChanged(nameof(ShowReferenceGroup));
        RaisePropertyChanged(nameof(CanSelectDraftChannelConfiguration));
    }

    private void ReportCommandError(Exception exception)
    {
        StatusText = exception.Message;
        notifications.PublishError(exception.Message);
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.ViewModels;

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
    private bool isRefreshingChannelConfigurations;
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public MontageConfigurationWorkspaceViewModel(
        ChannelConfigurationWorkspaceViewModel channelConfigurations,
        MontageProfileStore? store = null,
        OperationNotificationCenter? notifications = null)
    {
        this.channelConfigurations = channelConfigurations;
        this.store = store ?? new MontageProfileStore();
        this.notifications = notifications ?? new OperationNotificationCenter();
        this.channelConfigurations.ProfilesChanged += OnChannelConfigurationProfilesChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCommandError);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, ReportCommandError);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, ReportCommandError);
        ApplyAverageReferenceToAllCommand = new AsyncRelayCommand(
            () => { ApplyAverageReferenceToAll(); return Task.CompletedTask; },
            ReportCommandError);
        ApplyOriginalReferenceToAllCommand = new AsyncRelayCommand(
            () => { ApplyOriginalReferenceToAll(); return Task.CompletedTask; },
            ReportCommandError);
        ApplySpecifiedPairToAllCommand = new AsyncRelayCommand(
            () => { ApplySpecifiedPairToAll(); return Task.CompletedTask; },
            ReportCommandError);
    }

    public ObservableCollection<MontageProfile> Profiles { get; } = [];

    public ObservableCollection<MontageProfile> AvailableForAcquisition { get; } = [];

    public ObservableCollection<ChannelConfigurationProfile> AvailableChannelConfigurations { get; } = [];

    public ObservableCollection<MontageDraftRow> DraftRows { get; } = [];

    public ObservableCollection<AverageReferenceChannelOption> AverageReferenceChannels { get; } = [];

    public IReadOnlyList<MontageNegativeKindOption> NegativeKinds { get; } =
    [
        new(MontageNegativeKind.OriginalHardwareReference, "原始硬件参考"),
        new(MontageNegativeKind.Channel, "指定通道"),
        new(MontageNegativeKind.Mean, "平均参考"),
        new(MontageNegativeKind.SpecifiedPair, "指定双参考"),
    ];

    public ICommand RefreshCommand { get; }

    public ICommand SaveDraftCommand { get; }

    public ICommand DeleteSelectedCommand { get; }

    public ICommand ApplyAverageReferenceToAllCommand { get; }

    public ICommand ApplyOriginalReferenceToAllCommand { get; }

    public ICommand ApplySpecifiedPairToAllCommand { get; }

    public MontageProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                RaisePropertyChanged(nameof(CanDeleteSelected));
            }
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

    public bool IsEditingExisting => draftProfileId is not null;

    public bool CanEditDraft => !isReadOnlyDraft;

    public string DraftTitle => CanEditDraft
        ? IsEditingExisting ? "编辑导联配置" : "新建导联配置"
        : "查看导联配置";

    public bool ShowReferenceGroup => CanEditDraft || DraftRows.Any(row =>
        row.NegativeKind is MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair);

    public bool CanSelectDraftChannelConfiguration => CanEditDraft && !IsEditingExisting;

    public bool CanDeleteSelected => SelectedProfile?.CanDelete == true;

    public string DraftName { get => draftName; set => SetProperty(ref draftName, value); }

    public string DraftDescription { get => draftDescription; set => SetProperty(ref draftDescription, value); }

    public ChannelConfigurationProfile? DraftChannelConfiguration
    {
        get => draftChannelConfiguration;
        set
        {
            if (SetProperty(ref draftChannelConfiguration, value))
            {
                RaisePropertyChanged(nameof(DraftChannelSummary));
                if (!IsEditingExisting && value is not null)
                {
                    BuildFixedRows(value, null);
                }
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

    public bool CanApplyAverageReference => GetAverageReferenceLabels().Count >= 2 && DraftRows.Count > 0;

    public bool CanApplySpecifiedPair => GetAverageReferenceLabels().Count == 2 && DraftRows.Count > 0;

    public void BeginNewProfile()
    {
        isReadOnlyDraft = false;
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
        isReadOnlyDraft = profile.Source == MontageProfileSource.System;
        draftProfileId = profile.Source == MontageProfileSource.User ? profile.Id : null;
        draftCreatedAtUtc = profile.Source == MontageProfileSource.User ? profile.CreatedAtUtc : null;
        DraftName = profile.Name;
        DraftDescription = profile.Description;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelSummary));
        if (isReadOnlyDraft)
        {
            BuildReadOnlyRows(profile);
            StatusText = "系统默认导联配置仅供查看；如需修改，请使用“复制”。";
        }
        else
        {
            BuildFixedRows(profile.ChannelConfigurationSnapshot, profile);
            StatusText = "源通道和顺序由通道配置固定；本页只编辑重参考规则。";
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
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = $"{profile.Name} 副本";
        DraftDescription = profile.Description;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelSummary));
        BuildFixedRows(profile.ChannelConfigurationSnapshot, profile);
        StatusText = $"已从“{profile.Name}”创建副本。";
        RaiseDraftModePropertiesChanged();
    }

    public void CloseDraft()
    {
        isReadOnlyDraft = false;
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
            throw new InvalidOperationException("系统默认导联配置只能查看，不能修改。");
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
        var previousByPositive = existing?.DerivedChannels
            .GroupBy(channel => channel.PositiveLabel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? [];
        DraftRows.Clear();
        foreach (var (label, order) in fixedChannels)
        {
            previousByPositive.TryGetValue(label, out var prior);
            var kind = prior?.NegativeKind ?? MontageNegativeKind.OriginalHardwareReference;
            var selectedNegative = kind == MontageNegativeKind.Channel
                ? prior?.NegativeLabels.SingleOrDefault()
                : null;
            var selectedPair = kind == MontageNegativeKind.SpecifiedPair
                ? prior?.NegativeLabels.Take(2).ToArray() ?? []
                : [];
            DraftRows.Add(new MontageDraftRow(
                label,
                order,
                fixedChannels.Select(channel => channel.Item1).ToArray(),
                kind,
                selectedNegative,
                selectedPair.ElementAtOrDefault(0),
                selectedPair.ElementAtOrDefault(1),
                AverageReferenceSummary,
                SpecifiedPairSummary));
        }
    }

    private void BuildReadOnlyRows(MontageProfile profile)
    {
        var sourceLabels = profile.ChannelConfigurationSnapshot.Channels
            .Where(channel => channel.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(channel.ElectrodeLabel))
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.ElectrodeLabel.Trim())
            .ToArray();
        var averageLabels = profile.AverageReferenceLabels ?? [];
        BuildAverageReferenceOptions(sourceLabels, averageLabels);
        DraftRows.Clear();
        foreach (var channel in profile.DerivedChannels.OrderBy(channel => channel.DisplayOrder))
        {
            var negativeLabels = channel.NegativeLabels.Take(2).ToArray();
            DraftRows.Add(new MontageDraftRow(
                channel.PositiveLabel,
                channel.DisplayOrder,
                sourceLabels,
                channel.NegativeKind,
                channel.NegativeKind == MontageNegativeKind.Channel ? negativeLabels.SingleOrDefault() : null,
                channel.NegativeKind == MontageNegativeKind.SpecifiedPair ? negativeLabels.ElementAtOrDefault(0) : null,
                channel.NegativeKind == MontageNegativeKind.SpecifiedPair ? negativeLabels.ElementAtOrDefault(1) : null,
                AverageReferenceSummary,
                SpecifiedPairSummary));
        }
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
        RaisePropertyChanged(nameof(CanApplyAverageReference));
        RaisePropertyChanged(nameof(CanApplySpecifiedPair));
        foreach (var row in DraftRows)
        {
            row.UpdateReferenceSummaries(AverageReferenceSummary, SpecifiedPairSummary);
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

    private MontageProfile PrepareSourceStatus(MontageProfile profile)
    {
        var snapshotFingerprint = ChannelConfigurationFingerprint.Create(profile.ChannelConfigurationSnapshot);
        var montageFingerprint = MontageProfileFingerprint.Create(
            snapshotFingerprint,
            profile.DerivedChannels,
            profile.AverageReferenceLabels);
        if (!string.Equals(snapshotFingerprint, profile.ChannelConfigurationFingerprint, StringComparison.Ordinal) ||
            !string.Equals(montageFingerprint, profile.Fingerprint, StringComparison.Ordinal))
        {
            return profile with
            {
                ChannelSnapshotStatusLabel = "配置无效",
                ChannelSnapshotStatusDetail = "保存的通道快照或导联内容与其指纹不一致，不能作为可信配置使用。",
            };
        }

        var currentDeviceSignature = channelConfigurations.CurrentDeviceSignature;
        if (currentDeviceSignature is not null &&
            !string.Equals(profile.DeviceSignature, currentDeviceSignature, StringComparison.Ordinal))
        {
            return profile with
            {
                ChannelSnapshotStatusLabel = "设备不匹配",
                ChannelSnapshotStatusDetail = "此导联配置绑定的物理通道能力与当前设备不同。",
            };
        }

        var source = channelConfigurations.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, profile.ChannelConfigurationSnapshot.Id, StringComparison.Ordinal));
        if (source is null)
        {
            return profile with
            {
                ChannelSnapshotStatusLabel = "来源已删除",
                ChannelSnapshotStatusDetail = "原通道配置已删除；导联仍保留创建时的完整通道快照。",
            };
        }

        var currentFingerprint = ChannelConfigurationFingerprint.Create(source);
        var isCurrent = string.Equals(
            currentFingerprint,
            profile.ChannelConfigurationFingerprint,
            StringComparison.Ordinal);
        return profile with
        {
            ChannelSnapshotStatusLabel = isCurrent ? "已同步" : "来源已修改",
            ChannelSnapshotStatusDetail = isCurrent
                ? "导联绑定的通道快照与当前通道配置一致。"
                : "原通道配置已有未同步修改；此导联继续使用创建时保存的旧快照。",
        };
    }

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
            RefreshAvailableChannelConfigurations();
            Profiles.Clear();
            foreach (var profile in CreateSystemProfiles())
            {
                Profiles.Add(PrepareSourceStatus(profile));
            }
            foreach (var profile in await store.LoadAsync(CancellationToken.None))
            {
                Profiles.Add(PrepareSourceStatus(profile));
            }
            AvailableForAcquisition.Clear();
            foreach (var profile in Profiles.Where(profile => profile.ChannelSnapshotStatusLabel == "已同步"))
            {
                AvailableForAcquisition.Add(profile);
            }
            SelectedProfile = selectedId is null
                ? Profiles.FirstOrDefault()
                : Profiles.FirstOrDefault(profile => profile.Id == selectedId);
            StatusText = $"已载入 {Profiles.Count} 份导联配置；可用通道配置 {AvailableChannelConfigurations.Count} 份。";
            RaisePropertyChanged(nameof(AvailableForAcquisition));
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is not { CanDelete: true } profile)
        {
            throw new InvalidOperationException("系统预设导联配置不能删除。");
        }
        await store.DeleteAsync(profile.Id, CancellationToken.None);
        SelectedProfile = null;
        await RefreshAsync();
        StatusText = $"已删除导联配置“{profile.Name}”。";
        notifications.PublishSuccess($"已删除导联配置“{profile.Name}”。");
    }

    private void RaiseDraftModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsEditingExisting));
        RaisePropertyChanged(nameof(CanEditDraft));
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

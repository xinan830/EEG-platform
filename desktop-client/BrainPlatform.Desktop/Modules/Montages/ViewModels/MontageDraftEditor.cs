using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// Sole owner of the montage draft state, row models, average/bipolar reference
/// option subscriptions, and profile validation/serialization snapshot creation.
/// </summary>
internal sealed class MontageDraftEditor : ObservableObject
{
    private string draftName = string.Empty;
    private string draftDescription = string.Empty;
    private ChannelConfigurationProfile? draftChannelConfiguration;
    private string? draftProfileId;
    private DateTimeOffset? draftCreatedAtUtc;
    private bool isReadOnlyDraft;
    private string draftPermissionText = "可编辑";
    private bool hasSparseOutputTopology;

    public MontageDraftEditor()
    {
        OutputOrder = new MontageOutputOrder(DraftRows, () => CanEditDraft);
    }

    public ObservableCollection<ChannelConfigurationProfile> AvailableChannelConfigurations { get; } = [];

    public ObservableCollection<MontageDraftRow> DraftRows { get; } = [];

    public MontageOutputOrder OutputOrder { get; }

    public ObservableCollection<AverageReferenceChannelOption> AverageReferenceChannels { get; } = [];

    public bool IsSessionDraft { get; private set; }

    public string? DraftProfileId => draftProfileId;

    public bool IsEditingExisting => draftProfileId is not null;

    public bool CanEditDraft => !isReadOnlyDraft;

    public string DraftPermissionText => draftPermissionText;

    public string DraftTitle => CanEditDraft
        ? IsEditingExisting ? "编辑导联配置" : "新建导联配置"
        : "查看导联配置";

    public bool ShowReferenceGroup => CanEditDraft || DraftRows.Any(row =>
        row.NegativeKind is MontageNegativeKind.Mean or MontageNegativeKind.SpecifiedPair);

    public bool CanSelectDraftChannelConfiguration => !IsSessionDraft && CanEditDraft && !IsEditingExisting;

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
        : $"{DraftChannelConfiguration.DisplayName} · {DraftChannelConfiguration.EnabledChannelCount} 个固定源通道";

    public string AverageReferenceSummary => $"AVG（{AverageReferenceChannels.Count(option => option.IsIncluded)} 通道）";

    public string SpecifiedPairSummary => GetAverageReferenceLabels().Count == 2
        ? $"双参考（{string.Join("、", GetAverageReferenceLabels())}）"
        : "请选择两个通道";

    public bool CanEditReferenceMode => CanEditDraft && !hasSparseOutputTopology;

    public bool CanApplyOriginalReference => CanEditReferenceMode && DraftRows.Count > 0;

    public bool CanApplyAverageReference => CanEditReferenceMode && GetAverageReferenceLabels().Count >= 2 && DraftRows.Count > 0;

    public bool CanApplySpecifiedPair => CanEditReferenceMode && GetAverageReferenceLabels().Count == 2 && DraftRows.Count > 0;

    public string BeginNew(IEnumerable<ChannelConfigurationProfile> sourceProfiles)
    {
        IsSessionDraft = false;
        isReadOnlyDraft = false;
        draftPermissionText = "可编辑";
        hasSparseOutputTopology = false;
        RefreshAvailableChannelConfigurations(sourceProfiles);
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftChannelConfiguration = null;
        DraftRows.Clear();
        ClearAverageReferenceOptions();
        DraftChannelConfiguration = AvailableChannelConfigurations.FirstOrDefault();
        RaiseDraftModePropertiesChanged();
        return DraftChannelConfiguration is null
            ? "请先保存一份有效、与当前设备兼容且含显示通道的通道配置。"
            : "每个源通道均固定生成一行；请选择其显示重参考方式。";
    }

    public string BeginEdit(MontageProfile profile)
    {
        isReadOnlyDraft = !profile.CanEdit;
        draftPermissionText = profile.EditPermissionText;
        hasSparseOutputTopology = !MontageDraftRowBuilder.HasOneOutputPerConfiguredSource(profile);
        draftProfileId = profile.Source == MontageProfileSource.User ? profile.Id : null;
        draftCreatedAtUtc = profile.Source == MontageProfileSource.User ? profile.CreatedAtUtc : null;
        DraftName = profile.DisplayName;
        DraftDescription = profile.DisplayDescription;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        EnsureDraftChannelConfigurationOption(profile.ChannelConfigurationSnapshot);
        RaiseChannelConfigurationChanged();
        string status;
        if (isReadOnlyDraft)
        {
            var (sourceLabels, averageLabels, outputs) = MontageDraftRowBuilder.PrepareFromExisting(profile);
            BuildAverageReferenceOptions(sourceLabels, averageLabels);
            ReplaceDraftRows(profile.ChannelConfigurationSnapshot, outputs, averageLabels);
            status = $"{DraftPermissionText}；如需修改，请复制为新配置。";
        }
        else
        {
            BuildEditableRows(profile);
            status = "源通道由通道配置固定；可编辑参考规则、颜色和显示顺序。";
        }

        RaiseDraftModePropertiesChanged();
        return status;
    }

    public string BeginCopy(MontageProfile profile)
    {
        isReadOnlyDraft = false;
        draftPermissionText = "可编辑";
        hasSparseOutputTopology = !MontageDraftRowBuilder.HasOneOutputPerConfiguredSource(profile);
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftName = $"{profile.DisplayName} 副本";
        DraftDescription = profile.DisplayDescription;
        DraftChannelConfiguration = null;
        draftChannelConfiguration = profile.ChannelConfigurationSnapshot;
        EnsureDraftChannelConfigurationOption(profile.ChannelConfigurationSnapshot);
        RaiseChannelConfigurationChanged();
        BuildEditableRows(profile);
        RaiseDraftModePropertiesChanged();
        return $"已从“{profile.DisplayName}”创建副本。";
    }

    public string BeginSession(ChannelConfigurationProfile source, MontageProfile? existing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        IsSessionDraft = true;
        isReadOnlyDraft = false;
        draftPermissionText = "当前会话临时配置 · 仅波形显示";
        hasSparseOutputTopology = existing is not null && !MontageDraftRowBuilder.HasOneOutputPerConfiguredSource(existing);
        draftProfileId = existing?.Id ?? $"online-{Guid.NewGuid():N}";
        draftCreatedAtUtc = existing?.CreatedAtUtc;
        AvailableChannelConfigurations.Clear();
        AvailableChannelConfigurations.Add(source);
        draftChannelConfiguration = source;
        DraftName = existing?.DisplayName ?? "在线导联";
        DraftDescription = existing?.DisplayDescription ?? string.Empty;
        if (existing is null) BuildFixedRows(source, null);
        else BuildEditableRows(existing);
        RaiseDraftModePropertiesChanged();
        RaiseChannelConfigurationChanged();
        return "保存后自动切换整套波形导联；仅当前会话有效，不参与算法。";
    }

    public void Close()
    {
        IsSessionDraft = false;
        isReadOnlyDraft = false;
        hasSparseOutputTopology = false;
        draftProfileId = null;
        draftCreatedAtUtc = null;
        DraftRows.Clear();
        ClearAverageReferenceOptions();
        DraftChannelConfiguration = null;
        RaiseDraftModePropertiesChanged();
    }

    public void RefreshAvailableChannelConfigurations(IEnumerable<ChannelConfigurationProfile> sourceProfiles)
    {
        AvailableChannelConfigurations.Clear();
        foreach (var profile in sourceProfiles.Where(profile => profile.EnabledChannelCount > 0))
        {
            AvailableChannelConfigurations.Add(profile);
        }
    }

    public void SyncReadOnlyStateFromReloadedProfiles(IEnumerable<MontageProfile> reloadedProfiles)
    {
        var draftProfile = reloadedProfiles.FirstOrDefault(profile => profile.Id == draftProfileId);
        if (draftProfile is { CanEdit: false })
        {
            isReadOnlyDraft = true;
            draftPermissionText = draftProfile.EditPermissionText;
            RaiseDraftModePropertiesChanged();
        }
    }

    public MontageProfile BuildValidatedProfile(int? existingRevision)
    {
        if (!CanEditDraft)
        {
            throw new InvalidOperationException("此导联配置只读，不能修改；请复制为新配置。");
        }

        return MontageDraftRowBuilder.BuildValidatedProfile(
            DraftChannelConfiguration,
            DraftName,
            DraftDescription,
            DraftRows,
            GetAverageReferenceLabels(),
            draftProfileId,
            draftCreatedAtUtc,
            existingRevision);
    }

    public string ApplyAverageReferenceToAll()
    {
        if (!CanApplyAverageReference) throw new InvalidOperationException("请先在指定平均参考组中选择至少两个有效通道。");
        MontageDraftRowBuilder.ApplyReferenceToAll(DraftRows, MontageNegativeKind.Mean);
        return $"已将 {AverageReferenceSummary} 应用到全部 {DraftRows.Count} 个固定源通道。";
    }

    public string ApplyOriginalReferenceToAll()
    {
        if (!CanApplyOriginalReference) throw new InvalidOperationException("此导联配置的输出集合固定，不能整体切换参考方式。");
        MontageDraftRowBuilder.ApplyReferenceToAll(DraftRows, MontageNegativeKind.OriginalHardwareReference);
        return $"已恢复全部 {DraftRows.Count} 个通道的原始硬件参考显示。";
    }

    public string ApplySpecifiedPairToAll()
    {
        if (!CanApplySpecifiedPair) throw new InvalidOperationException("指定双参考必须选择恰好两个有效通道。");
        var pair = GetAverageReferenceLabels();
        MontageDraftRowBuilder.ApplyReferenceToAll(DraftRows, MontageNegativeKind.SpecifiedPair, pair);
        return $"已将指定双参考（{string.Join("、", pair)}）应用到全部 {DraftRows.Count} 个固定源通道。";
    }

    private void BuildFixedRows(ChannelConfigurationProfile source, MontageProfile? existing)
    {
        var (sourceLabels, averageLabels, outputs) = MontageDraftRowBuilder.PrepareFixed(source, existing);
        BuildAverageReferenceOptions(sourceLabels, averageLabels);
        ReplaceDraftRows(source, outputs, averageLabels);
    }

    private void BuildEditableRows(MontageProfile profile)
    {
        if (MontageDraftRowBuilder.HasOneOutputPerConfiguredSource(profile))
        {
            BuildFixedRows(profile.ChannelConfigurationSnapshot, profile);
            return;
        }

        var (sourceLabels, averageLabels, outputs) = MontageDraftRowBuilder.PrepareFromExisting(profile);
        BuildAverageReferenceOptions(sourceLabels, averageLabels);
        ReplaceDraftRows(profile.ChannelConfigurationSnapshot, outputs, averageLabels);
    }

    private void ReplaceDraftRows(
        ChannelConfigurationProfile source,
        IReadOnlyList<DerivedMontageChannel> outputs,
        IReadOnlyList<string> averageLabels)
    {
        DraftRows.Clear();
        foreach (var row in MontageDraftRowFactory.Create(source, outputs, averageLabels,
                     AverageReferenceSummary, SpecifiedPairSummary))
        {
            DraftRows.Add(row);
        }
    }

    private void BuildAverageReferenceOptions(IEnumerable<string> sourceLabels, IReadOnlyList<string> selectedLabels)
    {
        ClearAverageReferenceOptions();
        var selected = selectedLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var label in sourceLabels)
        {
            var option = new AverageReferenceChannelOption(label, selected.Contains(label));
            option.PropertyChanged += OnAverageReferenceOptionChanged;
            AverageReferenceChannels.Add(option);
        }

        UpdateAverageReferenceSummary();
    }

    private void ClearAverageReferenceOptions()
    {
        foreach (var option in AverageReferenceChannels)
        {
            option.PropertyChanged -= OnAverageReferenceOptionChanged;
        }

        AverageReferenceChannels.Clear();
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

    private void RaiseChannelConfigurationChanged()
    {
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelConfigurationId));
        RaisePropertyChanged(nameof(DraftChannelSummary));
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
}

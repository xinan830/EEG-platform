namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

public sealed partial class MontageConfigurationWorkspaceViewModel
{
    public bool IsSessionDraft { get; private set; }
    public event EventHandler<MontageProfile>? SessionProfileSaved;

    /// <summary>Edits a whole profile without library writes or device refresh.</summary>
    public void BeginSessionProfile(ChannelConfigurationProfile source, MontageProfile? existing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        IsSessionDraft = true;
        isReadOnlyDraft = false;
        draftPermissionText = "当前会话临时配置 · 仅波形显示";
        hasSparseOutputTopology = existing is not null && !HasOneOutputPerConfiguredSource(existing);
        draftProfileId = existing?.Id ?? $"online-{Guid.NewGuid():N}";
        draftCreatedAtUtc = existing?.CreatedAtUtc;
        SelectedProfile = null;
        AvailableChannelConfigurations.Clear();
        AvailableChannelConfigurations.Add(source);
        draftChannelConfiguration = source;
        DraftName = existing?.Name ?? "在线导联";
        DraftDescription = existing?.Description ?? string.Empty;
        if (existing is null) BuildFixedRows(source, null);
        else BuildEditableRows(existing);
        StatusText = "保存后自动切换整套波形导联；仅当前会话有效，不参与算法。";
        RaiseDraftModePropertiesChanged();
        RaisePropertyChanged(nameof(DraftChannelConfiguration));
        RaisePropertyChanged(nameof(DraftChannelConfigurationId));
        RaisePropertyChanged(nameof(DraftChannelSummary));
    }
}

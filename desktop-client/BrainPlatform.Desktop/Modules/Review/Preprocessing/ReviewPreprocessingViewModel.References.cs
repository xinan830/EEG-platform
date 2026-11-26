using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

public sealed class ArtifactReferenceChoice : ObservableObject
{
    private bool isEog;
    private string identity;
    private readonly Action? identityChanged;
    private readonly Action changed;
    public ArtifactReferenceChoice(string name, Action changed, bool isEog = false, string identity = "unconfirmed", Action? identityChanged = null)
    { Name = name; this.changed = changed; this.isEog = isEog; this.identity = identity; this.identityChanged = identityChanged; }
    public string Name { get; }
    public static IReadOnlyList<KeyValuePair<string, string>> IdentityOptions { get; } =
    [
        new("eeg", "EEG 脑电（参与脑电分析，可作眼电代理）"),
        new("eog", "专用 EOG 眼电（独立眼电，移出 EEG）"),
        new("ecg", "专用 ECG 心电（独立心电，移出 EEG）"),
        new("unconfirmed", "待确认（暂不参与 EEG）"),
    ];
    public string Identity
    {
        get => identity;
        set
        {
            if (!IdentityOptions.Any(option => option.Key == value)) throw new ArgumentException("无效通道身份。");
            if (!SetProperty(ref identity, value)) return;
            RaisePropertyChanged(nameof(ShortIdentityBadge));
            RaisePropertyChanged(nameof(HasNonEegBadge));
            identityChanged?.Invoke();
        }
    }
    public string ShortIdentityBadge => identity switch
    {
        "eog" => "专用EOG",
        "ecg" => "专用ECG",
        "unconfirmed" => "待确认",
        _ => "",
    };
    public bool HasNonEegBadge => identity != "eeg";
    public bool IsEog
    {
        get => isEog;
        set { if (SetProperty(ref isEog, value)) changed(); }
    }
}

public sealed partial class ReviewPreprocessingViewModel
{
    private string selectedEcgChannel = "";
    private bool isUpdatingReferenceChoices;
    private string referenceSourceSha256 = "";
    private bool hasUnsavedChannelIdentities;
    public bool HasUnsavedChannelIdentities => hasUnsavedChannelIdentities;
    public bool AreScientificInputsBlocked => ScientificInputBlockReason() is not null;
    private string? ScientificInputBlockReason(bool forIca = false)
    {
        if (HasUnsavedChannelIdentities) return "通道身份尚未保存，请先保存身份配置。";
        if (HasReferenceSelectionConflict) return "参考冲突：同一路不能同时作为眼电和心电参考，请重新选择。";
        if (ReferenceChannels.Any(row => row.IsEog && row.Identity is not ("eeg" or "eog")))
            return "眼电评分请选择 EEG 代理或专用 EOG，并保存身份。";
        if (!IsRemovalIca && !forIca) return null;
        if (ReferenceChannels.Any(row => SelectedEcgChannels.Contains(row.Name) && row.Identity != "ecg"))
            return "心电评分必须选择专用 ECG，并保存身份。";
        if (SelectedEcgChannels.Count == 0 && ReferenceChannels.Count(row => row.Identity == "ecg") > 1)
            return "检测到多路专用 ECG，请在心电评分中指定一路";
        return null;
    }
    public System.Windows.Input.ICommand SaveChannelIdentitiesCommand { get; private set; } = null!;
    public System.Windows.Input.ICommand DiscardUnsavedChannelIdentitiesCommand { get; private set; } = null!;
    private string referenceStatusText = "留空将按自动 / 前额代理策略计算；勾选仅用于伪迹打分，不改变通道身份。";
    public ObservableCollection<ArtifactReferenceChoice> ReferenceChannels { get; } = [];
    public ObservableCollection<string> EcgChannelOptions { get; } = [""];
    public IReadOnlyList<string> SelectedEogChannels => ReferenceChannels.Where(c => c.IsEog).Select(c => c.Name).ToArray();
    public IReadOnlyList<string> SelectedEcgChannels => string.IsNullOrEmpty(selectedEcgChannel) ? [] : [selectedEcgChannel];
    public bool HasReferenceSelectionConflict => SelectedEogChannels.Intersect(SelectedEcgChannels, StringComparer.OrdinalIgnoreCase).Any();
    public bool HasUnconfirmedChannelIdentities => ReferenceChannels.Any(row => row.Identity == "unconfirmed");
    public bool HasDedicatedEogChannels => ReferenceChannels.Any(row => row.Identity == "eog");
    public bool HasDedicatedEcgChannels => ReferenceChannels.Any(row => row.Identity == "ecg");
    public string ChannelIdentitySummaryText
    {
        get
        {
            if (ReferenceChannels.Count == 0) return "尚未读取录音通道";
            var eeg = ReferenceChannels.Count(row => row.Identity == "eeg");
            var eog = ReferenceChannels.Count(row => row.Identity == "eog");
            var ecg = ReferenceChannels.Count(row => row.Identity == "ecg");
            var unconfirmed = ReferenceChannels.Count(row => row.Identity == "unconfirmed");
            var summary = $"EEG {eeg} 路 · 专用 EOG {eog} 路 · 专用 ECG {ecg} 路";
            if (unconfirmed > 0) return $"{summary} · 待确认 {unconfirmed} 路";
            return eog == 0 && ecg == 0 ? $"{summary}（常规脑电帽无需修改）" : summary;
        }
    }
    public string EcgSelectorHintText => !IsRemovalIca
        ? "仅在启用 Infomax ICA 时生效"
        : !HasDedicatedEcgChannels
            ? "当前录音未标记专用 ECG；若接有外置心电电极，请在上方「配置通道身份…」中标记并保存"
            : "留空为自动匹配已标记的专用 ECG；有多路专用 ECG 时请指定一路";
    public string ReferenceStatusText { get => referenceStatusText; private set => SetProperty(ref referenceStatusText, value); }
    public string SelectedEcgChannel
    {
        get => selectedEcgChannel;
        set
        {
            if (isUpdatingReferenceChoices) return;
            value ??= "";
            if (!string.IsNullOrEmpty(value) && !EcgChannelOptions.Contains(value))
                throw new ArgumentException("心电参考不在当前录音通道中。");
            if (!SetProperty(ref selectedEcgChannel, value)) return;
            ReferenceSelectionChanged();
        }
    }

    public async Task LoadReferenceChannelsAsync()
    {
        var operation = BeginInspection();
        try
        {
            var (id, _, _) = await ensureBackendRecordingAsync(operation.Token);
            var response = await RequireClient().GetArtifactReferenceChannelsAsync(id, operation.Token);
            EnsureInspectionCurrent(operation);
            if (HasUnsavedChannelIdentities) return;
            referenceSourceSha256 = response.SourceSha256;
            var chosen = SelectedEogChannels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ecg = selectedEcgChannel;
            isUpdatingReferenceChoices = true;
            try
            {
                ReferenceChannels.Clear();
                var options = response.Channels.Select(channel => channel.Name).Prepend("").ToArray();
                // Keep surviving option objects so WPF retains its selected item and display content.
                for (var i = EcgChannelOptions.Count - 1; i >= 0; i--)
                    if (!options.Contains(EcgChannelOptions[i])) EcgChannelOptions.RemoveAt(i);
                for (var i = 0; i < options.Length; i++)
                {
                    var existing = EcgChannelOptions.IndexOf(options[i]);
                    if (existing < 0) EcgChannelOptions.Insert(i, options[i]);
                    else if (existing != i) EcgChannelOptions.Move(existing, i);
                }
                foreach (var channel in response.Channels)
                {
                    ReferenceChannels.Add(new ArtifactReferenceChoice(channel.Name, ReferenceSelectionChanged, chosen.Contains(channel.Name),
                        channel.Identity, ChannelIdentityChanged));
                }
                selectedEcgChannel = EcgChannelOptions.Contains(ecg) ? ecg : "";
            }
            finally { isUpdatingReferenceChoices = false; }
            RaisePropertyChanged(nameof(SelectedEcgChannel));
            UpdateReferenceValidationState();
            if (!chosen.SetEquals(SelectedEogChannels) || ecg != selectedEcgChannel) ReferenceSelectionChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (operation.Token.IsCancellationRequested) { }
        catch (Exception ex) { ReferenceStatusText = $"辅助通道读取失败：{ex.Message}"; }
        finally { EndInspection(operation); }
    }

    private void ReferenceSelectionChanged()
    {
        InvalidateIcaFingerprint("辅助通道已变更，需重新审查 ICA 成分");
        InvalidateArtifactInspection();
        NotifyConfigAndVisuals();
    }

    private void UpdateReferenceValidationState()
    {
        RaisePropertyChanged(nameof(HasReferenceSelectionConflict));
        RaisePropertyChanged(nameof(AreScientificInputsBlocked));
        RaisePropertyChanged(nameof(HasUnconfirmedChannelIdentities));
        RaisePropertyChanged(nameof(HasDedicatedEogChannels));
        RaisePropertyChanged(nameof(HasDedicatedEcgChannels));
        RaisePropertyChanged(nameof(IsEogRegressionMissingDedicatedEog));
        RaisePropertyChanged(nameof(ChannelIdentitySummaryText));
        RaisePropertyChanged(nameof(EcgSelectorHintText));
        var reason = ScientificInputBlockReason();
        ReferenceStatusText = reason ??
            $"眼电评分：{string.Join(", ", SelectedEogChannels.DefaultIfEmpty("自动 / 代理"))} · 心电评分：{string.Join(", ", SelectedEcgChannels.DefaultIfEmpty("自动识别"))}";
        if (reason is not null)
        {
            StatusMessage = reason;
            if (IsOverlayCompareEnabled) OverlayStatusText = reason;
        }
    }

    private void ValidateReferenceSelection(bool forIca = false)
    {
        if (ScientificInputBlockReason(forIca) is { } error) throw new InvalidOperationException(error);
    }

    private void ResetReferenceChannels()
    {
        isUpdatingReferenceChoices = true;
        try
        {
            ReferenceChannels.Clear();
            EcgChannelOptions.Clear();
            EcgChannelOptions.Add("");
            selectedEcgChannel = "";
            hasUnsavedChannelIdentities = false;
            referenceSourceSha256 = "";
        }
        finally { isUpdatingReferenceChoices = false; }
        RaisePropertyChanged(nameof(SelectedEcgChannel));
        RaisePropertyChanged(nameof(HasReferenceSelectionConflict));
        RaisePropertyChanged(nameof(HasUnsavedChannelIdentities));
        RaisePropertyChanged(nameof(AreScientificInputsBlocked));
        RaisePropertyChanged(nameof(HasUnconfirmedChannelIdentities));
        RaisePropertyChanged(nameof(HasDedicatedEogChannels));
        RaisePropertyChanged(nameof(HasDedicatedEcgChannels));
        RaisePropertyChanged(nameof(IsEogRegressionMissingDedicatedEog));
        RaisePropertyChanged(nameof(ChannelIdentitySummaryText));
        RaisePropertyChanged(nameof(EcgSelectorHintText));
    }

    private void ChannelIdentityChanged()
    {
        hasUnsavedChannelIdentities = true;
        RaisePropertyChanged(nameof(HasUnsavedChannelIdentities));
        RaisePropertyChanged(nameof(AreScientificInputsBlocked));
        ReferenceSelectionChanged();
    }

    public async Task DiscardUnsavedChannelIdentitiesAsync()
    {
        if (!HasUnsavedChannelIdentities) return;
        hasUnsavedChannelIdentities = false;
        RaisePropertyChanged(nameof(HasUnsavedChannelIdentities));
        await LoadReferenceChannelsAsync();
    }

    public async Task SaveChannelIdentitiesAsync()
    {
        if (!HasUnsavedChannelIdentities) return;
        var operation = BeginInspection();
        try
        {
            var (id, _, _) = await ensureBackendRecordingAsync(operation.Token);
            var update = new ChannelIdentityUpdate(referenceSourceSha256, ReferenceChannels
                .Select(row => new ChannelIdentitySelection(row.Name, row.Identity)).ToArray());
            var response = await RequireClient().SaveChannelIdentitiesAsync(id, update, operation.Token);
            EnsureInspectionCurrent(operation);
            hasUnsavedChannelIdentities = false;
            referenceSourceSha256 = response.SourceSha256;
            RaisePropertyChanged(nameof(HasUnsavedChannelIdentities));
            RaisePropertyChanged(nameof(AreScientificInputsBlocked));
            ReferenceSelectionChanged();
            if (!AreScientificInputsBlocked)
                ReferenceStatusText = "通道身份已保存到当前录音；EEG 代理保留脑电身份，专用 EOG/ECG 独立处理。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReferenceStatusText = $"身份保存失败：{ex.Message}"; }
        finally { EndInspection(operation); }
    }
}

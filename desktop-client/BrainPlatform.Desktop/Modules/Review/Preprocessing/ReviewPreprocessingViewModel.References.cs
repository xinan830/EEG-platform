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
        [new("unconfirmed", "待确认"), new("eeg", "EEG 脑电 / 可作眼电代理"), new("eog", "专用 EOG 眼电"), new("ecg", "专用 ECG 心电")];
    public string Identity
    {
        get => identity;
        set
        {
            if (!IdentityOptions.Any(option => option.Key == value)) throw new ArgumentException("无效通道身份。");
            if (SetProperty(ref identity, value)) identityChanged?.Invoke();
        }
    }
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
    public bool AreScientificInputsBlocked => HasUnsavedChannelIdentities || ReferenceValidationMessage is not null;
    private string? ReferenceValidationMessage => HasReferenceSelectionConflict
        ? "参考冲突：同一路不能同时作为眼电和心电参考，请重新选择。"
        : ReferenceChannels.Any(row => row.IsEog && row.Identity is not ("eeg" or "eog"))
            ? "眼电评分请选择 EEG 代理或专用 EOG，并保存身份。"
            : ReferenceChannels.Any(row => SelectedEcgChannels.Contains(row.Name) && row.Identity != "ecg")
                ? "心电评分必须选择专用 ECG，并保存身份。" : null;
    public System.Windows.Input.ICommand SaveChannelIdentitiesCommand { get; private set; } = null!;
    private string referenceStatusText = "可选择记录中的专用眼电、心电通道；留空使用现有自动识别策略。";
    public ObservableCollection<ArtifactReferenceChoice> ReferenceChannels { get; } = [];
    public ObservableCollection<string> EcgChannelOptions { get; } = [""];
    public IReadOnlyList<string> SelectedEogChannels => ReferenceChannels.Where(c => c.IsEog).Select(c => c.Name).ToArray();
    public IReadOnlyList<string> SelectedEcgChannels => string.IsNullOrEmpty(selectedEcgChannel) ? [] : [selectedEcgChannel];
    public bool HasReferenceSelectionConflict => SelectedEogChannels.Intersect(SelectedEcgChannels, StringComparer.OrdinalIgnoreCase).Any();
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
            ReferenceStatusText = HasReferenceSelectionConflict
                ? "参考冲突：同一路不能同时作为眼电和心电参考，请重新选择。"
                : $"已读取 {ReferenceChannels.Count} 路原始电压信号。请按实际接线选择，名称不代表生理用途。";
            if (!chosen.SetEquals(SelectedEogChannels) || ecg != selectedEcgChannel) ReferenceSelectionChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (operation.Token.IsCancellationRequested) { }
        catch (Exception ex) { ReferenceStatusText = $"辅助通道读取失败：{ex.Message}"; }
        finally { EndInspection(operation); }
    }

    private void ReferenceSelectionChanged()
    {
        RaisePropertyChanged(nameof(HasReferenceSelectionConflict));
        RaisePropertyChanged(nameof(AreScientificInputsBlocked));
        ReferenceStatusText = ReferenceValidationMessage ??
            $"眼电：{string.Join(", ", SelectedEogChannels.DefaultIfEmpty("自动 / 代理"))}；心电：{string.Join(", ", SelectedEcgChannels.DefaultIfEmpty("自动识别"))}";
        InvalidateIcaFingerprint("辅助通道已变更，需重新审查 ICA 成分");
        InvalidateArtifactInspection();
        if (AreScientificInputsBlocked)
        {
            StatusMessage = ReferenceStatusText;
            if (IsOverlayCompareEnabled) OverlayStatusText = "眼电与心电参考冲突，科学对比已暂停，请重新选择。";
        }
        NotifyConfigAndVisuals();
    }

    private void ValidateReferenceSelection()
    {
        if (HasUnsavedChannelIdentities) throw new InvalidOperationException("通道身份尚未保存，请先保存身份配置。");
        if (ReferenceValidationMessage is { } error) throw new InvalidOperationException(error);
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
    }

    private void ChannelIdentityChanged()
    {
        hasUnsavedChannelIdentities = true;
        RaisePropertyChanged(nameof(HasUnsavedChannelIdentities));
        RaisePropertyChanged(nameof(AreScientificInputsBlocked));
        ReferenceSelectionChanged();
        ReferenceStatusText = "身份已修改，请先保存；科学预览和算法已暂停。";
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
            ReferenceStatusText = "通道身份已保存到当前录音；EEG 代理保留脑电身份，专用 EOG/ECG 独立处理。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReferenceStatusText = $"身份保存失败：{ex.Message}"; }
        finally { EndInspection(operation); }
    }
}

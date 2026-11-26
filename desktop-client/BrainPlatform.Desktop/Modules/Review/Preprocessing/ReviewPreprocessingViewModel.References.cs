using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

public sealed class ArtifactReferenceChoice : ObservableObject
{
    private bool isEog;
    private readonly Action changed;
    public ArtifactReferenceChoice(string name, Action changed, bool isEog = false)
    { Name = name; this.changed = changed; this.isEog = isEog; }
    public string Name { get; }
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
    private string referenceStatusText = "可选择记录中的专用眼电、心电通道；留空使用现有自动识别策略。";
    public ObservableCollection<ArtifactReferenceChoice> ReferenceChannels { get; } = [];
    public ObservableCollection<string> EcgChannelOptions { get; } = [""];
    public IReadOnlyList<string> SelectedEogChannels => ReferenceChannels.Where(c => c.IsEog).Select(c => c.Name).ToArray();
    public IReadOnlyList<string> SelectedEcgChannels => string.IsNullOrEmpty(selectedEcgChannel) ? [] : [selectedEcgChannel];
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
                    ReferenceChannels.Add(new ArtifactReferenceChoice(channel.Name, ReferenceSelectionChanged, chosen.Contains(channel.Name)));
                }
                selectedEcgChannel = EcgChannelOptions.Contains(ecg) ? ecg : "";
            }
            finally { isUpdatingReferenceChoices = false; }
            RaisePropertyChanged(nameof(SelectedEcgChannel));
            ReferenceStatusText = $"已读取 {ReferenceChannels.Count} 路原始电压信号。请按实际接线选择，名称不代表生理用途。";
            if (!chosen.SetEquals(SelectedEogChannels) || ecg != selectedEcgChannel) ReferenceSelectionChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (operation.Token.IsCancellationRequested) { }
        catch (Exception ex) { ReferenceStatusText = $"辅助通道读取失败：{ex.Message}"; }
        finally { EndInspection(operation); }
    }

    private void ReferenceSelectionChanged()
    {
        ReferenceStatusText = SelectedEogChannels.Intersect(SelectedEcgChannels, StringComparer.OrdinalIgnoreCase).Any()
            ? "同一路不能同时作为眼电和心电参考，请重新选择。"
            : $"眼电：{string.Join(", ", SelectedEogChannels.DefaultIfEmpty("自动 / 代理"))}；心电：{string.Join(", ", SelectedEcgChannels.DefaultIfEmpty("自动识别"))}";
        InvalidateIcaFingerprint("辅助通道已变更，需重新审查 ICA 成分");
        InvalidateArtifactInspection();
        NotifyConfigAndVisuals();
    }

    private void ValidateReferenceSelection()
    {
        if (SelectedEogChannels.Intersect(SelectedEcgChannels, StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidOperationException("同一路不能同时作为眼电和心电参考，请重新选择。");
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
        }
        finally { isUpdatingReferenceChoices = false; }
        RaisePropertyChanged(nameof(SelectedEcgChannel));
    }
}

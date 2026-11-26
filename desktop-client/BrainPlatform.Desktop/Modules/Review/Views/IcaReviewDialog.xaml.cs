using System.Windows;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public partial class IcaReviewDialog : Window
{
    private readonly ReviewPreprocessingViewModel viewModel;
    private readonly IcaInspectionResponse inspection;

    public IcaReviewDialog(ReviewPreprocessingViewModel viewModel, IcaInspectionResponse inspection)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.inspection = inspection;
        ComponentsItemsControl.ItemsSource = viewModel.IcaComponents;

        var proxyLabel = inspection.EogChannelSource switch
        {
            "dedicated_eog" => "专用 EOG 导联",
            "frontal_eeg_proxy" => $"前额脑电代理 ({string.Join(", ", inspection.EogChannelsUsed)})",
            "explicit_channel" => $"指定通道 ({string.Join(", ", inspection.EogChannelsUsed)})",
            _ => "未匹配 EOG 参考",
        };
        HeaderSubtitleText.Text =
            $"共 {inspection.ComponentCount} 个独立成分 · 眼电参考：{proxyLabel} · 心电参考：{string.Join(", ", inspection.EcgChannelsUsed.DefaultIfEmpty("未匹配"))} · 请结合评分、波形与头皮分布确认剔除。";
        FingerprintText.Text = $"指纹: {inspection.IcaFitFingerprint ?? "预览模式"}";
        UpdateSelectionSummary();
    }

    public IReadOnlyList<int> SelectedExcludedComponents =>
        viewModel.IcaComponents.Where(c => c.IsExcluded).Select(c => c.ComponentIndex).OrderBy(i => i).ToArray();

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateSelectionSummary();

    private void OnResetRecommendedClick(object sender, RoutedEventArgs e)
    {
        var rec = inspection.RecommendedExcludeComponents.ToHashSet();
        foreach (var card in viewModel.IcaComponents)
            card.IsExcluded = rec.Contains(card.ComponentIndex);
        UpdateSelectionSummary();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void UpdateSelectionSummary()
    {
        var selected = SelectedExcludedComponents;
        SelectionSummaryText.Text = selected.Count == 0
            ? "当前未勾选任何成分（确认后将保留全部成分，不做 ICA 剔除）"
            : $"已勾选 {selected.Count} 个待剔除成分：{string.Join(", ", selected.Select(i => $"IC{i:00}"))}";
    }
}

using System.Windows;
using System.Windows.Media;
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
            $"共 {inspection.ComponentCount} 个独立成分 · 眼电参考：{proxyLabel} · 心电参考：{string.Join(", ", inspection.EcgChannelsUsed.DefaultIfEmpty("未匹配"))} · 悬停标签可查看完整 7 类概率分布。";
        FingerprintText.Text = $"指纹: {inspection.IcaFitFingerprint ?? "预览模式"}";
        ApplyClassifierBanner();
        UpdateFilterLabels();
        UpdateSelectionSummary();
    }

    public IReadOnlyList<int> SelectedExcludedComponents =>
        viewModel.IcaComponents.Where(c => c.IsExcluded).Select(c => c.ComponentIndex).OrderBy(i => i).ToArray();

    public static string FormatClassifierStatusMessage(IcaInspectionResponse inspection)
    {
        var diagnostics = new List<string>();
        if (inspection.RankCapped && inspection.RequestedComponentCount is { } req && inspection.EffectiveRank is { } rank)
            diagnostics.Add($"已按有效秩自动封顶成分数（请求 {req} → 实际 {inspection.ComponentCount}，秩 {rank}）");
        if (string.Equals(inspection.FitSampleSufficiency, "short_recording", StringComparison.OrdinalIgnoreCase)
            && inspection.FitDurationSeconds is { } dur)
            diagnostics.Add($"有效拟合时长偏短（{dur:0.0} s）");
        var diagSuffix = diagnostics.Count > 0 ? $" · {string.Join("；", diagnostics)}" : "";

        var band = inspection.FitBandpassHz is { Count: >= 2 }
            ? $"{inspection.FitBandpassHz[0]:0.#}–{inspection.FitBandpassHz[1]:0.#} Hz"
            : "1–100 Hz";

        var notchNote = inspection.FitNotchHz is { } notch && notch > 0
            ? $"，含 {notch:0.#} Hz 陷波"
            : "";

        if (string.Equals(inspection.ClassifierStatus, "advisory_only", StringComparison.OrdinalIgnoreCase)
            || inspection.IcaConverged == false)
        {
            var iterText = inspection.IcaIterations is { } nIter && inspection.IcaMaxIterations is { } maxIter
                ? $"（迭代 {nIter}/{maxIter}）"
                : "";
            return $"ICA 未完全收敛或收敛证据不足{iterText}，解混权重可能不稳定；已关闭自动预勾选，分类结果仅作人工参考{diagSuffix}。";
        }

        if (string.Equals(inspection.ClassifierStatus, "ready", StringComparison.OrdinalIgnoreCase))
            return $"ICLabel 智能分类已就绪（{band} 宽带拟合{notchNote}，Extended Infomax）· 已按高置信度预勾选疑似伪迹，请复核确认{diagSuffix}。";

        if (string.Equals(inspection.ClassifierStatus, "ready_limited", StringComparison.OrdinalIgnoreCase))
            return $"受采样率限制（仅拟合至 {band}），ICLabel 仅提供人工参考分类、未自动预勾选（高频肌电灵敏度可能下降），请结合热力图与波形复核{diagSuffix}。";

        var referenceStatus = inspection.EogChannelsUsed.Count > 0 || inspection.EcgChannelsUsed.Count > 0
            ? "当前使用可用的 EOG / ECG 相关系数评分"
            : "当前未使用 EOG / ECG 评分参考，不生成相关性剔除建议，请人工审查成分";
        if (string.Equals(inspection.ClassifierStatus, "unavailable", StringComparison.OrdinalIgnoreCase))
            return $"ICLabel 离线模型或推理组件当前不可用；{referenceStatus}{diagSuffix}。";

        var baseFallback = (inspection.ClassifierUnavailableReason ?? string.Empty).ToLowerInvariant() switch
        {
            "non_average_reference_montage" =>
                "当前导联不是拟合通道全集的平均参考（如 REF / 双极 / 部分电极平均），ICLabel 需完整平均参考 (CAR) 启用",
            "fit_samples_not_common_average" =>
                "当前拟合信号与完整平均参考 (CAR) 不一致，ICLabel 未启用，未对信号额外重参考",
            "electrode_3d_coordinates_incomplete" =>
                "当前存在缺失或重复 3D 标准电极坐标的通道，ICLabel 未启用",
            "sampling_rate_too_low_for_icalabel" =>
                "当前采样率低于 104 Hz，无法提取 ICLabel 高频特征",
            _ =>
                "ICLabel 未启用；需满足完整平均参考 (CAR)、采样率及 3D 电极坐标条件",
        };
        return $"{baseFallback}；{referenceStatus}{diagSuffix}。";
    }

    private void ApplyClassifierBanner()
    {
        ClassifierStatusText.Text = FormatClassifierStatusMessage(inspection);
        var isFullReady = string.Equals(inspection.ClassifierStatus, "ready", StringComparison.OrdinalIgnoreCase)
            && inspection.IcaConverged != false;
        if (isFullReady)
        {
            ClassifierBanner.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
            ClassifierBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));
            ClassifierStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
        }
        else
        {
            ClassifierBanner.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));
            ClassifierBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));
            ClassifierStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
        }
    }

    private void UpdateFilterLabels()
    {
        var total = viewModel.IcaComponents.Count;
        var recCount = viewModel.IcaComponents.Count(c => c.RecommendedExclude);
        FilterAllRadio.Content = $"全部 ({total})";
        FilterRecommendedRadio.Content = $"仅看建议剔除 ({recCount})";
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (ComponentsItemsControl is null) return;
        ComponentsItemsControl.ItemsSource = FilterRecommendedRadio.IsChecked == true
            ? viewModel.IcaComponents.Where(c => c.RecommendedExclude).ToArray()
            : viewModel.IcaComponents;
    }

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

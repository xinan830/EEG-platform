using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;
using BrainPlatform.Desktop.Modules.Review.Views;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewIcaLabelDialogTests
{
    [Fact]
    public void IcaComponentCard_FormatsChineseIclabelBadgesAndFullSevenClassTooltip()
    {
        var json = """
            {
              "component_index": 3,
              "eog_score": 0.91,
              "ecg_score": null,
              "explained_variance_ratio": 0.24,
              "iclabel_label": "eye blink",
              "iclabel_probability": 0.9621,
              "iclabel_probabilities": {
                "brain": 0.0112,
                "muscle artifact": 0.0085,
                "eye blink": 0.9621,
                "heart beat": 0.0021,
                "line noise": 0.0004,
                "channel noise": 0.0042,
                "other": 0.0115
              },
              "recommendation_reasons": ["iclabel:eye_blink:0.96"],
              "recommended_exclude": true,
              "excluded": true
            }
            """;

        var item = JsonSerializer.Deserialize<IcaComponentItem>(json)!;
        var card = new IcaComponentCardViewModel(item, initialExcluded: true);

        Assert.True(card.HasIclabel);
        Assert.Equal("眼电 96%", card.ScoreBadgeText);
        Assert.Contains("眼电伪迹（96.2%）", card.ScoreTooltip);
        Assert.Contains("大脑脑电：1.1%", card.ScoreTooltip);
        Assert.Contains("单导噪声：0.4%", card.ScoreTooltip);
        Assert.Contains("EOG r=+0.91", card.ScoreTooltip);
        Assert.Contains("推荐依据：ICLabel 眼电伪迹 (0.96)", card.ScoreTooltip);

        var chNoise = new IcaComponentCardViewModel(
            new IcaComponentItem(14, null, null, 0.05, true, true, IclabelLabel: "channel noise", IclabelProbability: 0.89),
            true);
        Assert.Equal("单导噪声 89%", chNoise.ScoreBadgeText);

        var brain = new IcaComponentCardViewModel(
            new IcaComponentItem(0, 0.04, null, 0.31, false, false, IclabelLabel: "brain", IclabelProbability: 0.91),
            false);
        Assert.Equal("大脑 91%", brain.ScoreBadgeText);

        var muscle = new IcaComponentCardViewModel(
            new IcaComponentItem(7, null, null, 0.09, true, true, IclabelLabel: "muscle artifact", IclabelProbability: 0.82),
            true);
        Assert.Equal("肌电 82%", muscle.ScoreBadgeText);

        var other = new IcaComponentCardViewModel(
            new IcaComponentItem(9, null, null, 0.03, false, false, IclabelLabel: "other", IclabelProbability: 0.64),
            false);
        Assert.Equal("其他 64%", other.ScoreBadgeText);
    }

    [Fact]
    public void IcaComponentCard_FallsBackGracefullyWhenIclabelAbsent()
    {
        var fallbackItem = new IcaComponentItem(1, 0.84, null, 0.18, true, true);
        var card = new IcaComponentCardViewModel(fallbackItem, initialExcluded: true);

        Assert.False(card.HasIclabel);
        Assert.Equal("建议剔除 · EOG r=+0.84", card.ScoreBadgeText);
        Assert.Contains("EOG r=+0.84", card.ScoreTooltip);
    }

    [Fact]
    public void IcaReviewDialog_DisplaysClassifierStatusFilterAndResetCorrectly()
    {
        RunInSta(() =>
        {
            var readyResponse = new IcaInspectionResponse(
                "rec-1", "ica_infomax", 250, 5000, 3,
                ["Fp1-AVG"], "frontal_eeg_proxy", [],
                [1], [1], "fp-iclabel-ready", false, true,
                [
                    new IcaComponentItem(0, 0.05, null, 0.42, false, false, IclabelLabel: "brain", IclabelProbability: 0.93),
                    new IcaComponentItem(1, 0.92, null, 0.28, true, true, IclabelLabel: "eye blink", IclabelProbability: 0.96),
                    new IcaComponentItem(2, 0.11, null, 0.08, false, false, IclabelLabel: "other", IclabelProbability: 0.61),
                ],
                IcaAlgorithm: "extended_infomax",
                FitBandpassHz: [1.0, 100.0],
                FitBandLimitedByNyquist: false,
                ClassifierMethod: "mne_icalabel_onnx",
                ClassifierStatus: "ready",
                ClassifierUnavailableReason: null,
                ClassifierVersion: "mne-icalabel-0.10.0-onnx");

            var vm = new ReviewPreprocessingViewModel(
                _ => Task.FromResult(("rec-1", (string?)"montage-car", 20.0)),
                () => null,
                _ => { },
                () => (0.0, 10.0, 1.0, 50.0, 50.0));
            foreach (var comp in readyResponse.Components)
                vm.IcaComponents.Add(new IcaComponentCardViewModel(comp, comp.Excluded));

            var dialog = new IcaReviewDialog(vm, readyResponse);
            try
            {
                var statusText = (TextBlock)dialog.FindName("ClassifierStatusText");
                var infoBadge = (Border)dialog.FindName("ClassifierInfoBadge");
                Assert.Contains("ICLabel 智能分类已就绪", statusText.Text);
                Assert.Contains("1–100 Hz 宽带拟合", statusText.Text);
                Assert.Contains("Extended Infomax", statusText.Text);
                Assert.Contains("已按高置信度预勾选", Assert.IsType<string>(infoBadge.ToolTip));
                Assert.Contains("含 50 Hz 陷波", IcaReviewDialog.FormatClassifierStatusMessage(readyResponse with { FitNotchHz = 50.0 }));

                var filterAll = (RadioButton)dialog.FindName("FilterAllRadio");
                var filterRec = (RadioButton)dialog.FindName("FilterRecommendedRadio");
                var itemsControl = (ItemsControl)dialog.FindName("ComponentsItemsControl");
                Assert.Equal("全部 (3)", filterAll.Content);
                Assert.Equal("仅看建议剔除 (1)", filterRec.Content);
                var panel = Assert.IsType<System.Windows.Controls.Primitives.UniformGrid>(itemsControl.ItemsPanel.LoadContent());
                Assert.Equal(4, panel.Columns);

                // Toggle recommended-only filter and verify hidden manual selection is preserved
                vm.IcaComponents[0].IsExcluded = true;
                filterRec.IsChecked = true;
                var visible = Assert.IsAssignableFrom<IEnumerable<IcaComponentCardViewModel>>(itemsControl.ItemsSource);
                Assert.Single(visible);
                Assert.Equal([0, 1], dialog.SelectedExcludedComponents);

                // Reset recommended restores only IC01 across all components
                var resetBtn = FindButtonByContent((DependencyObject)dialog.Content, "恢复系统推荐");
                Assert.NotNull(resetBtn);
                resetBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([1], dialog.SelectedExcludedComponents);
            }
            finally
            {
                dialog.Close();
                vm.Dispose();
            }

            // Verify REF fallback banner text
            var refResponse = readyResponse with
            {
                ClassifierMethod = "eog_ecg_correlation",
                ClassifierStatus = "fallback_eog_ecg",
                ClassifierUnavailableReason = "non_average_reference_montage",
            };
            var msg = IcaReviewDialog.FormatClassifierStatusMessage(refResponse);
            Assert.Contains("平均参考 (CAR)", msg);
            Assert.Contains("EOG / ECG", msg);
            Assert.Contains("非完整平均参考 (CAR)", IcaReviewDialog.FormatClassifierSummaryMessage(refResponse));
            var noReferenceResponse = refResponse with { EogChannelsUsed = [], EcgChannelsUsed = [] };
            Assert.Contains("不生成相关性剔除建议", IcaReviewDialog.FormatClassifierStatusMessage(noReferenceResponse));
            Assert.Contains("未对信号额外重参考", IcaReviewDialog.FormatClassifierStatusMessage(noReferenceResponse with
                { ClassifierUnavailableReason = "fit_samples_not_common_average" }));
            Assert.Contains("离线模型或推理组件当前不可用", IcaReviewDialog.FormatClassifierStatusMessage(noReferenceResponse with
                { ClassifierStatus = "unavailable", ClassifierUnavailableReason = "icalabel_model_unavailable" }));

            // Verify Nyquist-limited (128 Hz) advisory status and rank capping diagnostics
            var limitedResponse = readyResponse with
            {
                FitBandpassHz = [1.0, 62.0],
                FitBandLimitedByNyquist = true,
                ClassifierStatus = "ready_limited",
                ClassifierUnavailableReason = "fit_band_limited_by_nyquist",
                RequestedComponentCount = 20,
                EffectiveRank = 18,
                RankCapped = true,
            };
            var limitedMsg = IcaReviewDialog.FormatClassifierStatusMessage(limitedResponse);
            Assert.Contains("受采样率限制", limitedMsg);
            Assert.Contains("未自动预勾选", limitedMsg);
            Assert.Contains("有效秩自动封顶", limitedMsg);

            // Verify non-converged ICA advisory warning
            var nonConverged = readyResponse with
            {
                ClassifierStatus = "advisory_only",
                ClassifierUnavailableReason = "ica_not_converged",
                IcaConverged = false,
                IcaIterations = 500,
                IcaMaxIterations = 500,
            };
            var nonConvMsg = IcaReviewDialog.FormatClassifierStatusMessage(nonConverged);
            Assert.Contains("未完全收敛", nonConvMsg);
            Assert.Contains("500/500", nonConvMsg);
            Assert.Contains("关闭自动预勾选", nonConvMsg);

            // Verify brain-protection advisory conflict formatting in tooltip
            var protectedBrain = new IcaComponentCardViewModel(
                new IcaComponentItem(
                    0, 0.84, null, 0.35, false, false,
                    IclabelLabel: "brain",
                    IclabelProbability: 0.94,
                    AdvisoryConflicts: ["eog_correlation_conflicts_with_brain:0.84"]),
                false);
            Assert.Contains("复核提示（已受保护，未自动勾选）", protectedBrain.ScoreTooltip);
            Assert.Contains("EOG 相关 |r|=0.84 与 大脑脑电 分类冲突", protectedBrain.ScoreTooltip);
        });
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        if (root is Button button && Equals(button.Content, content)) return button;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindButtonByContent(System.Windows.Media.VisualTreeHelper.GetChild(root, i), content);
            if (found is not null) return found;
        }
        if (root is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                var found = FindButtonByContent(child, content);
                if (found is not null) return found;
            }
        }
        else if (root is Decorator decorator && decorator.Child is not null)
            return FindButtonByContent(decorator.Child, content);
        else if (root is ContentControl cc && cc.Content is DependencyObject dep)
            return FindButtonByContent(dep, content);
        return null;
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

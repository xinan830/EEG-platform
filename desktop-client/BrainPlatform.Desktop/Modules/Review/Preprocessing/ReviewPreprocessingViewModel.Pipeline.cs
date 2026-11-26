namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Snapshot of current Review recording and filter metadata shown in the preprocessing flowchart.</summary>
public sealed record PreprocessingRecordingContext(
    string RecordingName,
    int ChannelCount,
    int SamplingRateHz,
    double DurationSeconds,
    string AnalysisMontageName,
    double DisplayHighPassHz,
    double DisplayLowPassHz,
    double DisplayNotchHz);

public sealed partial class ReviewPreprocessingViewModel
{
    private readonly Func<PreprocessingRecordingContext>? getRecordingContext;
    private bool icaInvalidatedPendingReview;

    public PreprocessingRecordingContext RecordingContext =>
        getRecordingContext?.Invoke() ?? new PreprocessingRecordingContext("当前记录", 0, 0, 0, "平均参考 / 原始导联", 1, 30, 50);

    public string InputNodeSummary
    {
        get
        {
            var ctx = RecordingContext;
            if (ctx.ChannelCount <= 0 && ctx.SamplingRateHz <= 0)
                return "当前回放原始 EEG 信号";
            var dur = TimeSpan.FromSeconds(Math.Max(0, ctx.DurationSeconds));
            var durText = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
            return $"{ctx.ChannelCount} 导 · {ctx.SamplingRateHz} Hz · {durText}";
        }
    }

    public string InputNodeDetail =>
        $"记录「{RecordingContext.RecordingName}」为只读输入，所有预处理均在内存副本中执行，不修改原始采集文件。";

    public string ActiveMontageName =>
        string.IsNullOrWhiteSpace(RecordingContext.AnalysisMontageName)
            ? "原始采集导联"
            : RecordingContext.AnalysisMontageName;

    public string MontageNodeSummary => $"{ActiveMontageName} · 零相位带通/陷波";

    public string ScientificFilterSummary =>
        "下游科学分析自动按算法需求执行零相位带通与工频陷波 (Q)，保证频谱与相位不失真。";

    public string DisplayFilterSummary
    {
        get
        {
            var ctx = RecordingContext;
            var notch = ctx.DisplayNotchHz > 0 ? $"{ctx.DisplayNotchHz:0.#} Hz 陷波" : "陷波关闭";
            return $"顶部波形显示当前为 {ctx.DisplayHighPassHz:0.##}–{ctx.DisplayLowPassHz:0.##} Hz · {notch}（因果滤波，仅用于屏幕浏览）。";
        }
    }

    public int SelectedBadChannelCount => SelectedBadChannels.Count;
    public bool HasBadChannelRows => BadChannelRows.Count > 0;
    public bool HasArtifactSegments => ArtifactSegments.Count > 0;
    public int ArtifactSegmentCount => ArtifactSegments.Count;

    public string BadChannelNodeState
    {
        get
        {
            if (BadChannelPolicy != "none" && SelectedBadChannels.Count > 0) return "active";
            if (BadChannelRows.Any(r => r.IsBadCandidate)) return "warning";
            return "inactive";
        }
    }

    public string BadChannelNodeBadge => BadChannelPolicy switch
    {
        "interpolate" when SelectedBadChannels.Count > 0 => $"3D 插值 · {SelectedBadChannels.Count} 导",
        "exclude" when SelectedBadChannels.Count > 0 => $"CAR 排除 · {SelectedBadChannels.Count} 导",
        _ when BadChannelRows.Any(r => r.IsBadCandidate) => $"发现 {BadChannelRows.Count(r => r.IsBadCandidate)} 路疑似坏导",
        _ => "跳过",
    };

    public string BadChannelNodeHint => BadChannelPolicy switch
    {
        "interpolate" when SelectedBadChannels.Count > 0 =>
            $"插值通道：{FormatChannelPreview(SelectedBadChannels)}",
        "exclude" when SelectedBadChannels.Count > 0 =>
            $"排除通道：{FormatChannelPreview(SelectedBadChannels)}",
        _ when BadChannelRows.Count > 0 =>
            $"已体检 {BadChannelRows.Count} 路通道，当前保持不处理",
        _ => "3D 球面插值 / CAR 安全排除",
    };

    public string ArtifactNodeState => ArtifactPolicy != "none" ? "active" : "inactive";

    public string ArtifactNodeBadge => ArtifactPolicy switch
    {
        "reject" => ArtifactSegments.Count > 0 ? $"掩膜排除 · {ArtifactSegments.Count} 段" : "掩膜排除 · 已启用",
        "annotate" => ArtifactSegments.Count > 0 ? $"仅标注 · {ArtifactSegments.Count} 段" : "仅标注 · 已启用",
        _ => "跳过",
    };

    public string ArtifactNodeHint => ArtifactSegments.Count > 0
        ? $"爆幅 {PeakToPeakUv:0} µV · EMG z={MuscleZScoreThreshold:0.#}"
        : $"按通道爆幅 ({PeakToPeakUv:0} µV) / EMG (z={MuscleZScoreThreshold:0.#})";

    public string RemovalNodeState
    {
        get
        {
            if (RemovalMethod == "ica_infomax")
                return !string.IsNullOrWhiteSpace(icaFitFingerprint) ? "active" : "warning";
            if (RemovalMethod is "ssp_eog" or "eog_regression")
                return "active";
            return icaInvalidatedPendingReview ? "warning" : "inactive";
        }
    }

    public string RemovalNodeBadge => RemovalMethod switch
    {
        "ica_infomax" when !string.IsNullOrWhiteSpace(icaFitFingerprint) =>
            $"ICA 剔除 · {confirmedIcaExcluded?.Count ?? 0} 成分",
        "ica_infomax" => "待拟合审查",
        "ssp_eog" => "SSP 眼电投影",
        "eog_regression" => "EOG 回归",
        _ => icaInvalidatedPendingReview ? "需重新审查" : "跳过",
    };

    public string RemovalNodeHint => RemovalMethod switch
    {
        "ica_infomax" when !string.IsNullOrWhiteSpace(icaFitFingerprint) =>
            $"已锁定：{ReviewPreprocessingProjection.FormatComponentList(confirmedIcaExcluded ?? [])}",
        "ica_infomax" => "请点击拟合 ICA 并审查 2D 头皮热力图",
        "ssp_eog" => AllowFrontalEogProxy ? "MNE SSP（允许前额代理）" : "MNE SSP（仅专用 EOG）",
        "eog_regression" => "专用 EOG 通道线性回归",
        _ => "ICA 2D 热力图审查 / SSP / EOG",
    };

    public string RibbonToolTipText => HasAnyActivePreprocessing
        ? $"科学预处理已启用：{ActiveSummaryBadge}\n点击打开预处理流程弹窗进行调整"
        : "打开科学预处理流程弹窗（坏导修复、坏段质控、ICA 审查与叠图对比）";

    public bool IsBadChannelPolicyNone
    {
        get => BadChannelPolicy == "none";
        set { if (value) BadChannelPolicy = "none"; }
    }

    public bool IsBadChannelPolicyInterpolate
    {
        get => BadChannelPolicy == "interpolate";
        set { if (value) BadChannelPolicy = "interpolate"; }
    }

    public bool IsBadChannelPolicyExclude
    {
        get => BadChannelPolicy == "exclude";
        set { if (value) BadChannelPolicy = "exclude"; }
    }

    public bool IsArtifactPolicyNone
    {
        get => ArtifactPolicy == "none";
        set { if (value) ArtifactPolicy = "none"; }
    }

    public bool IsArtifactPolicyAnnotate
    {
        get => ArtifactPolicy == "annotate";
        set { if (value) ArtifactPolicy = "annotate"; }
    }

    public bool IsArtifactPolicyReject
    {
        get => ArtifactPolicy == "reject";
        set { if (value) ArtifactPolicy = "reject"; }
    }

    public bool IsRemovalNone
    {
        get => RemovalMethod == "none";
        set { if (value) RemovalMethod = "none"; }
    }

    public bool IsRemovalIca
    {
        get => RemovalMethod == "ica_infomax";
        set { if (value) RemovalMethod = "ica_infomax"; }
    }

    public bool IsRemovalSsp
    {
        get => RemovalMethod == "ssp_eog";
        set { if (value) RemovalMethod = "ssp_eog"; }
    }

    public bool IsRemovalEog
    {
        get => RemovalMethod == "eog_regression";
        set { if (value) RemovalMethod = "eog_regression"; }
    }

    public bool IsRemovalActive => !IsRemovalNone;
    public bool IsEogRegressionMissingDedicatedEog => IsRemovalEog && !HasDedicatedEogChannels;

    public void RefreshContextMetadata()
    {
        RaisePropertyChanged(nameof(RecordingContext));
        RaisePropertyChanged(nameof(InputNodeSummary));
        RaisePropertyChanged(nameof(InputNodeDetail));
        RaisePropertyChanged(nameof(ActiveMontageName));
        RaisePropertyChanged(nameof(MontageNodeSummary));
        RaisePropertyChanged(nameof(DisplayFilterSummary));
    }

    private void RaisePipelineNodeProperties()
    {
        RaisePropertyChanged(nameof(SelectedBadChannelCount));
        RaisePropertyChanged(nameof(HasBadChannelRows));
        RaisePropertyChanged(nameof(HasArtifactSegments));
        RaisePropertyChanged(nameof(ArtifactSegmentCount));
        RaisePropertyChanged(nameof(BadChannelNodeState));
        RaisePropertyChanged(nameof(BadChannelNodeBadge));
        RaisePropertyChanged(nameof(BadChannelNodeHint));
        RaisePropertyChanged(nameof(ArtifactNodeState));
        RaisePropertyChanged(nameof(ArtifactNodeBadge));
        RaisePropertyChanged(nameof(ArtifactNodeHint));
        RaisePropertyChanged(nameof(RemovalNodeState));
        RaisePropertyChanged(nameof(RemovalNodeBadge));
        RaisePropertyChanged(nameof(RemovalNodeHint));
        RaisePropertyChanged(nameof(RibbonToolTipText));
        RaisePropertyChanged(nameof(IsBadChannelPolicyNone));
        RaisePropertyChanged(nameof(IsBadChannelPolicyInterpolate));
        RaisePropertyChanged(nameof(IsBadChannelPolicyExclude));
        RaisePropertyChanged(nameof(IsArtifactPolicyNone));
        RaisePropertyChanged(nameof(IsArtifactPolicyAnnotate));
        RaisePropertyChanged(nameof(IsArtifactPolicyReject));
        RaisePropertyChanged(nameof(IsRemovalNone));
        RaisePropertyChanged(nameof(IsRemovalActive));
        RaisePropertyChanged(nameof(IsRemovalIca));
        RaisePropertyChanged(nameof(IsRemovalSsp));
        RaisePropertyChanged(nameof(IsRemovalEog));
        RaisePropertyChanged(nameof(IsEogRegressionMissingDedicatedEog));
    }

    private static string FormatChannelPreview(IReadOnlyList<string> channels) =>
        channels.Count <= 4
            ? string.Join(", ", channels)
            : $"{string.Join(", ", channels.Take(4))} 等 {channels.Count} 导";
}

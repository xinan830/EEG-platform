using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

public enum ChannelVisualPreprocessingState
{
    Normal,
    CandidateBad,
    Excluded,
    Interpolated,
}

public sealed record ChannelVisualBadge(
    ChannelVisualPreprocessingState State,
    string Tooltip);

public sealed record ArtifactTimelineMarker(
    double StartSeconds,
    double EndSeconds,
    string Reason,
    string Label,
    string Tooltip);

public sealed class BadChannelRowViewModel : ObservableObject
{
    private bool isSelected;

    public BadChannelRowViewModel(BadChannelInspectionItem item, Action onSelectionChanged, bool? initialSelected = null)
    {
        Channel = item.Channel;
        IsBadCandidate = item.IsBad;
        CanInterpolate = item.CanInterpolate;
        Reasons = item.Reasons;
        ReasonsSummary = item.Reasons.Count == 0 ? "正常" : string.Join(" / ", item.Reasons.Select(FormatReason));
        MetricsSummary = $"RMS {item.RmsUv ?? 0:0.0} µV · 高频比 {item.HfPowerRatio ?? 0:0.00} · 邻域相关 {item.MaxNeighborCorrelation ?? 0:0.00}";
        InterpolationHint = item.CanInterpolate ? "支持 3D 球面样条插值" : $"不可插值（{FormatInterpReason(item.InterpolationUnavailableReason)}）";
        isSelected = initialSelected ?? item.IsBad;
        OnSelectionChangedCallback = onSelectionChanged;
    }

    private Action OnSelectionChangedCallback { get; }
    public string Channel { get; }
    public bool IsBadCandidate { get; }
    public bool CanInterpolate { get; }
    public IReadOnlyList<string> Reasons { get; }
    public string ReasonsSummary { get; }
    public string MetricsSummary { get; }
    public string InterpolationHint { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value)) return;
            OnSelectionChangedCallback();
        }
    }

    public static string FormatReason(string code) => code switch
    {
        "flatline" => "平线/断连",
        "extreme_amplitude" => "极端幅值",
        "high_frequency_noise" or "high_freq_noise" => "高频噪声",
        "low_neighbor_correlation" => "邻域失相关",
        "excessive_nan_ratio" => "缺值过多",
        _ => code,
    };

    private static string FormatInterpReason(string? code) => code switch
    {
        "insufficient_good_3d_donors" => "可用好导少于 3 路",
        "bipolar_channel_not_interpolatable" => "双极导联",
        "electrode_3d_position_unavailable" => "缺少 3D 坐标",
        "non_eeg_channel" => "非 EEG 通道",
        _ => code ?? "不满足条件",
    };
}

public sealed class ArtifactSegmentRowViewModel
{
    public ArtifactSegmentRowViewModel(ArtifactSegmentItem item)
    {
        StartSeconds = item.StartSeconds;
        EndSeconds = item.EndSeconds;
        DurationSeconds = item.DurationSeconds;
        Reason = item.Reason;
        Channels = item.Channels;
        PeakMetric = item.PeakMetric;
        IsMuscle = string.Equals(item.Reason, "muscle_zscore", StringComparison.OrdinalIgnoreCase);
        ReasonLabel = item.Reason switch
        {
            "muscle_zscore" => "EMG 肌电",
            "amplitude_ptp" => "爆幅超限",
            _ => "手动坏段",
        };
        ShortBadge = item.Reason switch
        {
            "muscle_zscore" => $"EMG 肌电 {item.DurationSeconds:0.00}s",
            "amplitude_ptp" => item.PeakMetric is { } p ? $"爆幅 {p:0} µV" : $"爆幅超限 {item.DurationSeconds:0.00}s",
            _ => $"坏段 {item.DurationSeconds:0.00}s",
        };
        TimeRangeText = $"{TimeSpan.FromSeconds(item.StartSeconds):mm\\:ss\\.ff} - {TimeSpan.FromSeconds(item.EndSeconds):mm\\:ss\\.ff} ({item.DurationSeconds:0.00}s)";
        ChannelsSummary = item.Channels.Count == 0
            ? "全部通道"
            : item.Channels.Count <= 4
                ? string.Join(", ", item.Channels)
                : $"{string.Join(", ", item.Channels.Take(4))} 等 {item.Channels.Count} 导";
        TooltipText = $"{ReasonLabel} | {TimeRangeText}\n通道：{ChannelsSummary}" +
                      (item.PeakMetric is { } metric ? $"\n峰值指标：{metric:0.##}" : "");
    }

    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public double DurationSeconds { get; }
    public string Reason { get; }
    public bool IsMuscle { get; }
    public string ReasonLabel { get; }
    public string ShortBadge { get; }
    public string TimeRangeText { get; }
    public string ChannelsSummary { get; }
    public string TooltipText { get; }
    public IReadOnlyList<string> Channels { get; }
    public double? PeakMetric { get; }

    public ArtifactTimelineMarker ToMarker() => new(StartSeconds, EndSeconds, Reason, ShortBadge, TooltipText);
}

public sealed class IcaComponentCardViewModel : ObservableObject
{
    private static readonly string[] IclabelOrder =
    [
        "brain", "eye blink", "muscle artifact", "heart beat", "line noise", "channel noise", "other"
    ];

    private bool isExcluded;

    public IcaComponentCardViewModel(IcaComponentItem item, bool initialExcluded)
    {
        item.ValidatePreview();
        ComponentIndex = item.ComponentIndex;
        Title = $"IC{item.ComponentIndex:00}";
        EogScore = item.EogScore;
        EcgScore = item.EcgScore;
        ExplainedVarianceRatio = item.ExplainedVarianceRatio;
        RecommendedExclude = item.RecommendedExclude;
        IclabelLabel = item.IclabelLabel;
        IclabelProbability = item.IclabelProbability;
        IclabelProbabilities = item.IclabelProbabilities;
        RecommendationReasons = item.RecommendationReasons ?? [];
        AdvisoryConflicts = item.AdvisoryConflicts ?? [];
        Topomap = item.Topomap;
        PreviewWaveform = item.PreviewWaveform?.Select(value => value ?? double.NaN).ToArray() ?? [];
        PreviewTimesSeconds = item.PreviewTimesSeconds ?? [];
        PreviewDurationSeconds = item.PreviewDurationSeconds;
        isExcluded = initialExcluded;

        var scores = new List<string>();
        if (item.EogScore is { } eog) scores.Add($"EOG r={eog:+0.00;-0.00}");
        if (item.EcgScore is { } ecg) scores.Add($"ECG {ecg:0.00}");

        if (!string.IsNullOrWhiteSpace(item.IclabelLabel) && item.IclabelProbability is { } prob)
        {
            var shortName = FormatIclabelShort(item.IclabelLabel);
            ScoreBadgeText = $"{shortName} {prob * 100:0}%";
        }
        else
        {
            ScoreBadgeText = (item.RecommendedExclude ? "建议剔除 · " : "") +
                (scores.Count > 0 ? string.Join(" · ", scores) : "脑电/其他成分");
        }

        ScoreTooltip = BuildScoreTooltip(item, scores);
        EvrText = $"解释方差 {item.ExplainedVarianceRatio * 100:0.0}%";
        DetailHintText = HasIclabel && scores.Count > 0 ? string.Join(" · ", scores) : "10 s 成分时程";
        IsBrainClassification = string.Equals(item.IclabelLabel, "brain", StringComparison.OrdinalIgnoreCase);
    }

    public int ComponentIndex { get; }
    public string Title { get; }
    public double? EogScore { get; }
    public double? EcgScore { get; }
    public double ExplainedVarianceRatio { get; }
    public bool RecommendedExclude { get; }
    public bool IsBrainClassification { get; }
    public string? IclabelLabel { get; }
    public double? IclabelProbability { get; }
    public IReadOnlyDictionary<string, double>? IclabelProbabilities { get; }
    public IReadOnlyList<string> RecommendationReasons { get; }
    public IReadOnlyList<string> AdvisoryConflicts { get; }
    public bool HasIclabel => !string.IsNullOrWhiteSpace(IclabelLabel) && IclabelProbability is not null;
    public string ScoreBadgeText { get; }
    public string ScoreTooltip { get; }
    public string EvrText { get; }
    public string DetailHintText { get; }
    public TopomapField? Topomap { get; }
    public IReadOnlyList<double> PreviewWaveform { get; }
    public IReadOnlyList<double> PreviewTimesSeconds { get; }
    public double PreviewDurationSeconds { get; }

    public bool IsExcluded
    {
        get => isExcluded;
        set => SetProperty(ref isExcluded, value);
    }

    public static string FormatIclabelShort(string? label) => (label ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "brain" => "大脑",
        "eye blink" => "眼电",
        "muscle artifact" => "肌电",
        "heart beat" => "心电",
        "line noise" => "工频",
        "channel noise" => "单导噪声",
        "other" => "其他",
        _ => string.IsNullOrWhiteSpace(label) ? "未知" : label,
    };

    public static string FormatIclabelFull(string? label) => (label ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "brain" => "大脑脑电",
        "eye blink" => "眼电伪迹",
        "muscle artifact" => "肌电伪迹",
        "heart beat" => "心电伪迹",
        "line noise" => "工频噪声",
        "channel noise" => "单导噪声",
        "other" => "其他成分",
        _ => string.IsNullOrWhiteSpace(label) ? "未知成分" : label,
    };

    private static string BuildScoreTooltip(IcaComponentItem item, List<string> correlationScores)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.IclabelLabel) && item.IclabelProbability is { } prob)
        {
            lines.Add($"ICLabel 主分类：{FormatIclabelFull(item.IclabelLabel)}（{prob * 100:0.0}%）");
            if (item.IclabelProbabilities is { Count: > 0 } dist)
            {
                lines.Add("7 类概率分布：");
                foreach (var cls in IclabelOrder)
                {
                    if (dist.TryGetValue(cls, out var p))
                        lines.Add($"  · {FormatIclabelFull(cls)}：{p * 100:0.0}%");
                }
            }
        }
        if (correlationScores.Count > 0)
            lines.Add($"参考通道相关：{string.Join(" · ", correlationScores)}");
        if (item.RecommendationReasons is { Count: > 0 } reasons)
            lines.Add($"推荐依据：{string.Join("，", reasons.Select(FormatReasonCode))}");
        if (item.AdvisoryConflicts is { Count: > 0 } conflicts)
            lines.Add($"复核提示（已受保护，未自动勾选）：{string.Join("，", conflicts.Select(FormatConflictCode))}");
        if (lines.Count == 0)
            lines.Add("未启用 ICLabel 且未匹配参考通道，请结合头皮热力图与波形人工判断。");
        return string.Join("\n", lines);
    }

    private static string FormatConflictCode(string conflict)
    {
        var parts = conflict.Split(':');
        if (parts.Length == 2 && parts[0].StartsWith("eog_correlation_conflicts_with_", StringComparison.Ordinal))
            return $"EOG 相关 |r|={parts[1]} 与 {FormatIclabelFull(parts[0].Replace("eog_correlation_conflicts_with_", "").Replace('_', ' '))} 分类冲突";
        if (parts.Length == 2 && parts[0].StartsWith("ecg_correlation_conflicts_with_", StringComparison.Ordinal))
            return $"ECG 相关 {parts[1]} 与 {FormatIclabelFull(parts[0].Replace("ecg_correlation_conflicts_with_", "").Replace('_', ' '))} 分类冲突";
        if (parts.Length == 2 && parts[0] == "eog_correlation_requires_review")
            return $"EOG 相关 |r|={parts[1]}（拟合未收敛，需人工复核）";
        if (parts.Length == 2 && parts[0] == "ecg_correlation_requires_review")
            return $"ECG 相关 {parts[1]}（拟合未收敛，需人工复核）";
        return conflict;
    }

    private static string FormatReasonCode(string reason)
    {
        var parts = reason.Split(':');
        if (parts.Length == 3 && parts[0] == "iclabel")
            return $"ICLabel {FormatIclabelFull(parts[1].Replace('_', ' '))} ({parts[2]})";
        if (parts.Length == 2 && parts[0] == "eog_correlation")
            return $"EOG 相关 |r|={parts[1]}";
        if (parts.Length == 2 && parts[0] == "ecg_correlation")
            return $"ECG 相关 {parts[1]}";
        return reason;
    }
}

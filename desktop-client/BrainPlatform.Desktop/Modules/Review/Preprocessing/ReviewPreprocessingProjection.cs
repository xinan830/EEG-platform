using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Preprocessing;

/// <summary>Pure projection helpers mapping review preprocessing state to backend DTOs and UI badges.</summary>
internal static class ReviewPreprocessingProjection
{
    public static string FormatActiveSummaryBadge(
        string badChannelPolicy,
        int selectedBadChannelCount,
        string artifactPolicy,
        string removalMethod,
        string? icaFitFingerprint,
        IReadOnlyList<int>? confirmedIcaExcluded)
    {
        var parts = new List<string>();
        if (badChannelPolicy != "none" && selectedBadChannelCount > 0)
            parts.Add($"坏导:{selectedBadChannelCount}({(badChannelPolicy == "interpolate" ? "插值" : "排除")})");
        if (artifactPolicy != "none")
            parts.Add($"坏段:{(artifactPolicy == "reject" ? "剔除" : "标注")}");
        if (removalMethod == "ica_infomax" && !string.IsNullOrWhiteSpace(icaFitFingerprint))
            parts.Add($"ICA剔除:{(confirmedIcaExcluded?.Count ?? 0)}个");
        else if (removalMethod is "ssp_eog" or "eog_regression")
            parts.Add(removalMethod == "ssp_eog" ? "SSP去眼电" : "EOG回归");
        return parts.Count == 0 ? "未启用科学预处理" : string.Join(" · ", parts);
    }

    public static ChannelVisualBadge ResolveChannelBadge(
        string displayedChannelName,
        IReadOnlyList<BadChannelRowViewModel> rows,
        string badChannelPolicy,
        PreprocessingPreviewWindowResponse? preview)
    {
        if (badChannelPolicy == "exclude" &&
            preview?.Processed.ExcludedOutputChannels.Contains(displayedChannelName, StringComparer.OrdinalIgnoreCase) == true)
            return new ChannelVisualBadge(ChannelVisualPreprocessingState.Excluded,
                "该输出依赖已排除的电极或参考输入；浅灰为科学滤波基线，处理后输出已排除，原始记录未改变。");
        if (badChannelPolicy == "interpolate" &&
            preview?.Processed.InterpolatedChannels.Contains(displayedChannelName, StringComparer.OrdinalIgnoreCase) == true)
            return new ChannelVisualBadge(ChannelVisualPreprocessingState.Interpolated,
                "科学对比已应用球面样条插值；该输出依赖插值后的电极输入，原始记录未改变。");
        var baseName = StripMontageSuffix(displayedChannelName);
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.Channel, displayedChannelName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Channel, baseName, StringComparison.OrdinalIgnoreCase));
        if (row is null || (!row.IsSelected && !row.IsBadCandidate))
            return new ChannelVisualBadge(ChannelVisualPreprocessingState.Normal, "");

        return new ChannelVisualBadge(
            ChannelVisualPreprocessingState.CandidateBad,
            $"疑似坏导：{row.ReasonsSummary}\n{row.MetricsSummary}\n{row.InterpolationHint}" +
            (row.IsSelected && badChannelPolicy != "none" ? "\n已选择处理策略，待预览验证；原始波形未改变。" : ""));
    }

    public static OfflinePreprocessingConfigPayload BuildPayload(
        string badChannelPolicy,
        IReadOnlyList<string> selectedBadChannels,
        string artifactPolicy,
        double peakToPeakUv,
        double muscleZScoreThreshold,
        string removalMethod,
        bool allowFrontalEogProxy,
        int icaComponentCount,
        IReadOnlyList<int>? confirmedIcaExcluded,
        string? icaFitFingerprint,
        bool allowArtifactRemoval,
        IReadOnlyList<string>? eogChannels = null,
        IReadOnlyList<string>? ecgChannels = null)
    {
        var bads = badChannelPolicy != "none" ? selectedBadChannels : [];
        var effBadPolicy = bads.Count > 0 ? badChannelPolicy : "none";
        var badPayload = new BadChannelConfigPayload(effBadPolicy, bads);
        var artPayload = new ArtifactRejectionConfigPayload(
            artifactPolicy,
            artifactPolicy != "none" ? peakToPeakUv : null,
            artifactPolicy != "none" ? muscleZScoreThreshold : null);

        var effMethod = allowArtifactRemoval ? removalMethod : "none";
        if (effMethod == "ica_infomax" && string.IsNullOrWhiteSpace(icaFitFingerprint))
            effMethod = "none";

        var remPayload = new ArtifactRemovalConfigPayload(
            Method: effMethod,
            EogChannels: effMethod != "none" ? eogChannels ?? [] : [],
            AllowFrontalEogProxy: (eogChannels?.Count ?? 0) == 0 && allowFrontalEogProxy,
            IcaComponentCount: effMethod == "ica_infomax" ? icaComponentCount : null,
            IcaEogThreshold: 0.7,
            IcaExcludeComponents: effMethod == "ica_infomax" ? (confirmedIcaExcluded ?? []) : null,
            IcaFitFingerprint: effMethod == "ica_infomax" ? icaFitFingerprint : null,
            EcgChannels: effMethod == "ica_infomax" ? ecgChannels ?? [] : []);
        return new OfflinePreprocessingConfigPayload(badPayload, artPayload, remPayload);
    }

    public static Dictionary<string, object?>? ToAlgorithmConfigDictionary(OfflinePreprocessingConfigPayload payload)
    {
        if (payload.BadChannels.Policy == "none" &&
            payload.ArtifactRejection.Policy == "none" &&
            payload.ArtifactRemoval.Method == "none")
            return null;

        return new Dictionary<string, object?>
        {
            ["bad_channels"] = new Dictionary<string, object?>
            {
                ["policy"] = payload.BadChannels.Policy,
                ["channels"] = payload.BadChannels.Channels,
            },
            ["artifact_rejection"] = new Dictionary<string, object?>
            {
                ["policy"] = payload.ArtifactRejection.Policy,
                ["peak_to_peak_uv"] = payload.ArtifactRejection.PeakToPeakUv,
                ["amplitude_window_s"] = 0.5,
                ["muscle_zscore_threshold"] = payload.ArtifactRejection.MuscleZScoreThreshold,
                ["min_duration_s"] = 0.1,
                ["pad_s"] = 0.1,
                ["manual_segments"] = Array.Empty<object>(),
            },
            ["artifact_removal"] = new Dictionary<string, object?>
            {
                ["method"] = payload.ArtifactRemoval.Method,
                ["eog_channels"] = payload.ArtifactRemoval.EogChannels,
                ["ecg_channels"] = payload.ArtifactRemoval.EcgChannels ?? [],
                ["allow_frontal_eog_proxy"] = payload.ArtifactRemoval.AllowFrontalEogProxy,
                ["ica_n_components"] = payload.ArtifactRemoval.IcaComponentCount,
                ["ica_eog_threshold"] = payload.ArtifactRemoval.IcaEogThreshold,
                ["ica_exclude_components"] = payload.ArtifactRemoval.IcaExcludeComponents,
                ["ica_fit_fingerprint"] = payload.ArtifactRemoval.IcaFitFingerprint,
                ["ssp_n_eeg"] = payload.ArtifactRemoval.SspEegCount,
                ["random_state"] = 42,
            },
        };
    }

    public static string FormatComponentList(IReadOnlyList<int> indices) =>
        indices.Count == 0 ? "无" : string.Join(", ", indices.Select(i => $"IC{i:00}"));

    public static string FormatMuscleStatus(string status) => status switch
    {
        "mne_standard_110_140_hz" => "MNE 110–140 Hz 肌电",
        "adapted_high_freq_band" => "自适应高频肌电",
        "skipped_low_sampling_rate" => "采样率过低已跳过肌电",
        _ => status,
    };

    public static string FormatArtifactRemovalValidityNote(ArtifactRemovalValidity? validity)
    {
        if (validity is not { Intervals.Count: > 0 }) return string.Empty;
        var missing = validity.Intervals
            .SelectMany(row => row.MissingInputChannels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var additionalCount = validity.Intervals
            .SelectMany(row => row.AdditionalUnavailableChannels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var missingLabel = missing.Length == 0 ? "输入通道" : string.Join("、", missing);
        return additionalCount > 0
            ? $" · 去伪迹重建不可用（因 {missingLabel} 缺口致另 {additionalCount} 路不可用，非新增坏段）"
            : $" · 去伪迹重建不可用（因 {missingLabel} 缺口，非新增坏段）";
    }

    private static string StripMontageSuffix(string label)
    {
        var idx = label.IndexOf('-');
        return idx > 0 ? label[..idx].Trim() : label.Trim();
    }
}

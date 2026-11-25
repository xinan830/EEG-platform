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
        string badChannelPolicy)
    {
        var baseName = StripMontageSuffix(displayedChannelName);
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.Channel, displayedChannelName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Channel, baseName, StringComparison.OrdinalIgnoreCase));
        if (row is null || (!row.IsSelected && !row.IsBadCandidate))
            return new ChannelVisualBadge(ChannelVisualPreprocessingState.Normal, "");

        if (row.IsSelected && badChannelPolicy == "interpolate")
            return new ChannelVisualBadge(
                ChannelVisualPreprocessingState.Interpolated,
                $"已球面样条插值修复（原因：{row.ReasonsSummary}）\n{row.MetricsSummary}");
        if (row.IsSelected && badChannelPolicy == "exclude")
            return new ChannelVisualBadge(
                ChannelVisualPreprocessingState.Excluded,
                $"已排除坏导（原因：{row.ReasonsSummary}）\n{row.MetricsSummary}");
        return new ChannelVisualBadge(
            ChannelVisualPreprocessingState.CandidateBad,
            $"疑似坏导：{row.ReasonsSummary}\n{row.MetricsSummary}\n{row.InterpolationHint}");
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
        bool allowArtifactRemoval)
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
            EogChannels: [],
            AllowFrontalEogProxy: allowFrontalEogProxy,
            IcaComponentCount: effMethod == "ica_infomax" ? icaComponentCount : null,
            IcaEogThreshold: 0.7,
            IcaExcludeComponents: effMethod == "ica_infomax" ? (confirmedIcaExcluded ?? []) : null,
            IcaFitFingerprint: effMethod == "ica_infomax" ? icaFitFingerprint : null);
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
                ["muscle_zscore_threshold"] = payload.ArtifactRejection.MuscleZScoreThreshold,
            },
            ["artifact_removal"] = new Dictionary<string, object?>
            {
                ["method"] = payload.ArtifactRemoval.Method,
                ["eog_channels"] = payload.ArtifactRemoval.EogChannels,
                ["allow_frontal_eog_proxy"] = payload.ArtifactRemoval.AllowFrontalEogProxy,
                ["ica_n_components"] = payload.ArtifactRemoval.IcaComponentCount,
                ["ica_eog_threshold"] = payload.ArtifactRemoval.IcaEogThreshold,
                ["ica_exclude_components"] = payload.ArtifactRemoval.IcaExcludeComponents,
                ["ica_fit_fingerprint"] = payload.ArtifactRemoval.IcaFitFingerprint,
                ["ssp_n_eeg"] = payload.ArtifactRemoval.SspEegCount,
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

    private static string StripMontageSuffix(string label)
    {
        var idx = label.IndexOf('-');
        return idx > 0 ? label[..idx].Trim() : label.Trim();
    }
}

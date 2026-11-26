using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record BadChannelInspectionItem(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("is_eeg")] bool IsEeg,
    [property: JsonPropertyName("is_bad")] bool IsBad,
    [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons,
    [property: JsonPropertyName("nan_ratio")] double NanRatio,
    [property: JsonPropertyName("std_uv")] double? StdUv,
    [property: JsonPropertyName("ptp_uv")] double? PtpUv,
    [property: JsonPropertyName("robust_span_uv")] double? RobustSpanUv,
    [property: JsonPropertyName("rms_uv")] double? RmsUv,
    [property: JsonPropertyName("hf_power_ratio")] double? HfPowerRatio,
    [property: JsonPropertyName("max_neighbor_correlation")] double? MaxNeighborCorrelation,
    [property: JsonPropertyName("can_interpolate")] bool CanInterpolate,
    [property: JsonPropertyName("interpolation_unavailable_reason")] string? InterpolationUnavailableReason);

public sealed record BadChannelInspectionResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("sample_count")] long SampleCount,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds,
    [property: JsonPropertyName("montage_template")] string MontageTemplate,
    [property: JsonPropertyName("recommended_bad_channels")] IReadOnlyList<string> RecommendedBadChannels,
    [property: JsonPropertyName("channels")] IReadOnlyList<BadChannelInspectionItem> Channels);

public sealed record ArtifactSegmentItem(
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds,
    [property: JsonPropertyName("duration_s")] double DurationSeconds,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("peak_metric")] double? PeakMetric);

public sealed record ArtifactSegmentInspectionResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("sample_count")] long SampleCount,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds,
    [property: JsonPropertyName("clean_ratio")] double CleanRatio,
    [property: JsonPropertyName("rejected_sample_count")] long RejectedSampleCount,
    [property: JsonPropertyName("muscle_check_status")] string MuscleCheckStatus,
    [property: JsonPropertyName("muscle_band_hz")] IReadOnlyList<double>? MuscleBandHz,
    [property: JsonPropertyName("segments")] IReadOnlyList<ArtifactSegmentItem> Segments);

public sealed record IcaComponentItem(
    [property: JsonPropertyName("component_index")] int ComponentIndex,
    [property: JsonPropertyName("eog_score")] double? EogScore,
    [property: JsonPropertyName("ecg_score")] double? EcgScore,
    [property: JsonPropertyName("explained_variance_ratio")] double ExplainedVarianceRatio,
    [property: JsonPropertyName("recommended_exclude")] bool RecommendedExclude,
    [property: JsonPropertyName("excluded")] bool Excluded,
    [property: JsonPropertyName("topomap")] TopomapField? Topomap = null,
    [property: JsonPropertyName("preview_waveform")] IReadOnlyList<double?>? PreviewWaveform = null,
    [property: JsonPropertyName("preview_duration_s")] double PreviewDurationSeconds = 0,
    [property: JsonPropertyName("preview_times_s")] IReadOnlyList<double>? PreviewTimesSeconds = null,
    [property: JsonPropertyName("preview_unit")] string? PreviewUnit = null,
    [property: JsonPropertyName("preview_compression")] PreviewDisplayCompression? PreviewCompression = null)
{
    public void ValidatePreview()
    {
        if (PreviewWaveform is not { Count: > 0 } values) return;
        if (PreviewTimesSeconds is not { } times || times.Count != values.Count ||
            !double.IsFinite(PreviewDurationSeconds) || PreviewDurationSeconds <= 0 ||
            PreviewUnit != "z-score" || PreviewCompression is not
                { Method: "finite_run_min_max_envelope_v1", ScientificResampling: false })
            throw new InvalidOperationException("ICA 预览的时间、单位或包络契约无效。");
        for (var i = 0; i < times.Count; i++)
            if (!double.IsFinite(times[i]) || times[i] < 0 || times[i] >= PreviewDurationSeconds ||
                (i > 0 && times[i] <= times[i - 1]) || (values[i] is { } value && !double.IsFinite(value)))
                throw new InvalidOperationException("ICA 预览包含无效坐标或数值。");
    }
}

public sealed record IcaInspectionResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("sampling_rate_hz")] double SamplingRateHz,
    [property: JsonPropertyName("sample_count")] long SampleCount,
    [property: JsonPropertyName("n_components")] int ComponentCount,
    [property: JsonPropertyName("eog_channels_used")] IReadOnlyList<string> EogChannelsUsed,
    [property: JsonPropertyName("eog_channel_source")] string? EogChannelSource,
    [property: JsonPropertyName("ecg_channels_used")] IReadOnlyList<string> EcgChannelsUsed,
    [property: JsonPropertyName("recommended_exclude_components")] IReadOnlyList<int> RecommendedExcludeComponents,
    [property: JsonPropertyName("selected_exclude_components")] IReadOnlyList<int> SelectedExcludeComponents,
    [property: JsonPropertyName("ica_fit_fingerprint")] string? IcaFitFingerprint,
    [property: JsonPropertyName("ica_cache_hit")] bool IcaCacheHit,
    [property: JsonPropertyName("can_apply_to_continuous")] bool CanApplyToContinuous,
    [property: JsonPropertyName("components")] IReadOnlyList<IcaComponentItem> Components);

public sealed record BadChannelConfigPayload(
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels);

public sealed record ArtifactRejectionConfigPayload(
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("peak_to_peak_uv")] double? PeakToPeakUv,
    [property: JsonPropertyName("muscle_zscore_threshold")] double? MuscleZScoreThreshold);

public sealed record ArtifactRemovalConfigPayload(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("eog_channels")] IReadOnlyList<string> EogChannels,
    [property: JsonPropertyName("allow_frontal_eog_proxy")] bool AllowFrontalEogProxy,
    [property: JsonPropertyName("ica_n_components")] int? IcaComponentCount,
    [property: JsonPropertyName("ica_eog_threshold")] double IcaEogThreshold,
    [property: JsonPropertyName("ica_exclude_components")] IReadOnlyList<int>? IcaExcludeComponents,
    [property: JsonPropertyName("ica_fit_fingerprint")] string? IcaFitFingerprint,
    [property: JsonPropertyName("ssp_n_eeg")] int SspEegCount = 1,
    [property: JsonPropertyName("ecg_channels")] IReadOnlyList<string>? EcgChannels = null);

public sealed record OfflinePreprocessingConfigPayload(
    [property: JsonPropertyName("bad_channels")] BadChannelConfigPayload BadChannels,
    [property: JsonPropertyName("artifact_rejection")] ArtifactRejectionConfigPayload ArtifactRejection,
    [property: JsonPropertyName("artifact_removal")] ArtifactRemovalConfigPayload ArtifactRemoval);

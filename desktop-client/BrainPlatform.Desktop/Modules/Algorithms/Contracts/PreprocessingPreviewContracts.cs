using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record PreviewDisplayCompression(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("scientific_resampling")] bool ScientificResampling,
    [property: JsonPropertyName("target_points_per_channel")] int TargetPointsPerChannel,
    [property: JsonPropertyName("gap_boundaries_may_exceed_target")] bool GapBoundariesMayExceedTarget);

public sealed record PreviewFilterEvidence(
    [property: JsonPropertyName("preprocessing_scope")] string PreprocessingScope,
    [property: JsonPropertyName("filter_phase")] string FilterPhase,
    [property: JsonPropertyName("filter_bandpass_hz")] IReadOnlyList<double> FilterBandpassHz,
    [property: JsonPropertyName("notch_hz")] double? NotchHz,
    [property: JsonPropertyName("notch_quality_factor")] double? NotchQualityFactor,
    [property: JsonPropertyName("spec_fingerprint")] string SpecFingerprint)
{
    public void ValidateExpected(double lowHz, double highHz, double? notchHz, double notchQ)
    {
        if (FilterPhase != "zero_phase" ||
            FilterBandpassHz is not { Count: 2 } ||
            string.IsNullOrWhiteSpace(SpecFingerprint) ||
            Math.Abs(FilterBandpassHz[0] - lowHz) > 1e-6 ||
            Math.Abs(FilterBandpassHz[1] - highHz) > 1e-6)
            throw new InvalidOperationException("科学对比的共同滤波契约不一致。");
        if (notchHz is null)
        {
            if (NotchHz is not null)
                throw new InvalidOperationException("科学对比的陷波配置不一致。");
        }
        else if (NotchHz is null || Math.Abs(NotchHz.Value - notchHz.Value) > 1e-6 ||
                 NotchQualityFactor is null || Math.Abs(NotchQualityFactor.Value - notchQ) > 1e-6)
        {
            throw new InvalidOperationException("科学对比的陷波频率或 Q 值不一致。");
        }
    }
}

public sealed record PreviewBaselineSide(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("trace_times_s")] IReadOnlyDictionary<string, IReadOnlyList<double>> TraceTimesSeconds,
    [property: JsonPropertyName("traces_uv")] IReadOnlyDictionary<string, IReadOnlyList<double?>> TracesUv,
    [property: JsonPropertyName("preprocessing_evidence")] IReadOnlyDictionary<string, object?>? PreprocessingEvidence = null);

public sealed record PreviewProcessedSide(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels,
    [property: JsonPropertyName("excluded_output_channels")] IReadOnlyList<string> ExcludedOutputChannels,
    [property: JsonPropertyName("interpolated_channels")] IReadOnlyList<string> InterpolatedChannels,
    [property: JsonPropertyName("excluded_channels")] IReadOnlyList<string> ExcludedChannels,
    [property: JsonPropertyName("trace_times_s")] IReadOnlyDictionary<string, IReadOnlyList<double>> TraceTimesSeconds,
    [property: JsonPropertyName("traces_uv")] IReadOnlyDictionary<string, IReadOnlyList<double?>> TracesUv,
    [property: JsonPropertyName("ica_model_reused")] bool IcaModelReused,
    [property: JsonPropertyName("artifact_removal_status")] string ArtifactRemovalStatus,
    [property: JsonPropertyName("artifact_removal_validity")] ArtifactRemovalValidity? ArtifactRemovalValidity,
    [property: JsonPropertyName("preprocessing_evidence")] IReadOnlyDictionary<string, object?>? PreprocessingEvidence = null);

public sealed record PreprocessingPreviewWindowRequest(
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("window_s")] double WindowSeconds,
    [property: JsonPropertyName("montage_id")] string? MontageId,
    [property: JsonPropertyName("low_hz")] double LowHz,
    [property: JsonPropertyName("high_hz")] double HighHz,
    [property: JsonPropertyName("notch_hz")] double? NotchHz,
    [property: JsonPropertyName("max_points")] int MaxPoints,
    [property: JsonPropertyName("preprocessing")] OfflinePreprocessingConfigPayload Preprocessing,
    [property: JsonPropertyName("notch_q")] double NotchQ = 30.0);

public sealed record PreprocessingPreviewWindowResponse(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("source_sha256")] string? SourceSha256,
    [property: JsonPropertyName("montage_id")] string? MontageId,
    [property: JsonPropertyName("montage_snapshot_sha256")] string? MontageSnapshotSha256,
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds,
    [property: JsonPropertyName("sfreq_hz")] double SamplingRateHz,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("common_filter")] PreviewFilterEvidence CommonFilter,
    [property: JsonPropertyName("enhanced_preprocessing_active")] bool EnhancedPreprocessingActive,
    [property: JsonPropertyName("display_compression")] PreviewDisplayCompression DisplayCompression,
    [property: JsonPropertyName("baseline")] PreviewBaselineSide Baseline,
    [property: JsonPropertyName("processed")] PreviewProcessedSide Processed)
{
    public const string CurrentSchemaVersion = "scientific-preview-comparison-v1";
    public const string PairedCompressionMethod = "paired_finite_run_min_max_envelope_v1";

    public void Validate()
    {
        const double tolerance = 0.00001;
        if (SchemaVersion != CurrentSchemaVersion || Unit != "µV" ||
            DisplayCompression is not { Method: PairedCompressionMethod, ScientificResampling: false, TargetPointsPerChannel: >= 64 } ||
            CommonFilter is null || Baseline is null || Processed is null ||
            Baseline.Role != "scientific_baseline" || Processed.Role != "scientific_processed" ||
            Baseline.Channels is null || Baseline.Channels.Count == 0 || Processed.Channels is null ||
            Baseline.TraceTimesSeconds is null || Baseline.TracesUv is null ||
            Processed.TraceTimesSeconds is null || Processed.TracesUv is null ||
            Processed.ExcludedOutputChannels is null || Processed.InterpolatedChannels is null || Processed.ExcludedChannels is null ||
            StartSample < 0 || EndSample <= StartSample ||
            !double.IsFinite(StartSeconds) || !double.IsFinite(EndSeconds) || EndSeconds <= StartSeconds ||
            !double.IsFinite(SamplingRateHz) || SamplingRateHz <= 0 ||
            Math.Abs(StartSample / SamplingRateHz - StartSeconds) > tolerance ||
            Math.Abs(EndSample / SamplingRateHz - EndSeconds) > tolerance ||
            Baseline.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Baseline.Channels.Count ||
            Processed.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Processed.Channels.Count)
            throw new InvalidOperationException("科学对比预览的版本、压缩、时间或通道契约无效。");

        if (CommonFilter.FilterPhase != "zero_phase" || CommonFilter.FilterBandpassHz is not { Count: 2 } ||
            string.IsNullOrWhiteSpace(CommonFilter.SpecFingerprint) ||
            !double.IsFinite(CommonFilter.FilterBandpassHz[0]) || !double.IsFinite(CommonFilter.FilterBandpassHz[1]) ||
            CommonFilter.FilterBandpassHz[0] <= 0 || CommonFilter.FilterBandpassHz[1] <= CommonFilter.FilterBandpassHz[0])
            throw new InvalidOperationException("科学对比预览的共同滤波证据无效。");

        Processed.ArtifactRemovalValidity?.Validate(StartSeconds, EndSeconds, SamplingRateHz, Processed.Channels);

        var baselineLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in Baseline.Channels)
        {
            baselineLookup[ch] = ch;
            ValidateChannelSeries(ch, Baseline.TraceTimesSeconds, Baseline.TracesUv);
        }

        var processedMatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var procCh in Processed.Channels)
        {
            if (!baselineLookup.TryGetValue(procCh, out var baseCh))
                throw new InvalidOperationException($"科学对比处理侧包含基线不存在的通道：{procCh}。");
            processedMatched.Add(baseCh);
            ValidateChannelSeries(procCh, Processed.TraceTimesSeconds, Processed.TracesUv);
            var baseTimes = Baseline.TraceTimesSeconds[baseCh];
            var procTimes = Processed.TraceTimesSeconds[procCh];
            if (!baseTimes.SequenceEqual(procTimes))
                throw new InvalidOperationException($"科学对比通道 {procCh} 的基线与处理侧时间坐标不一致。");
            if (!EnhancedPreprocessingActive && !Baseline.TracesUv[baseCh].SequenceEqual(Processed.TracesUv[procCh]))
                throw new InvalidOperationException($"增强处理全部关闭时通道 {procCh} 的基线与处理侧样本不一致。");
        }

        var missingFromProcessed = Baseline.Channels.Where(ch => !processedMatched.Contains(ch)).ToArray();
        var declaredExcluded = new HashSet<string>(Processed.ExcludedOutputChannels, StringComparer.OrdinalIgnoreCase);
        if (!EnhancedPreprocessingActive && missingFromProcessed.Length > 0)
            throw new InvalidOperationException("增强处理全部关闭时处理侧缺少基线通道。");
        if (missingFromProcessed.Any(ch => !declaredExcluded.Contains(ch)))
            throw new InvalidOperationException("处理侧缺失的通道未在 excluded_output_channels 中显式声明。");
    }

    private void ValidateChannelSeries(
        string channel,
        IReadOnlyDictionary<string, IReadOnlyList<double>> timesMap,
        IReadOnlyDictionary<string, IReadOnlyList<double?>> valuesMap)
    {
        if (!timesMap.TryGetValue(channel, out var times) ||
            !valuesMap.TryGetValue(channel, out var values) ||
            times.Count != values.Count || times.Count == 0)
            throw new InvalidOperationException($"科学对比通道 {channel} 的时间与样本长度不一致。");
        for (var i = 0; i < times.Count; i++)
            if (!double.IsFinite(times[i]) || times[i] < StartSeconds - 0.000001 ||
                times[i] >= EndSeconds + 0.000001 || (i > 0 && times[i] <= times[i - 1]) ||
                (values[i] is { } value && !double.IsFinite(value)))
                throw new InvalidOperationException($"科学对比通道 {channel} 包含无效的绝对时间或样本。");
    }
}

using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record CustomFrequencyWindowPairRequest(
    [property: JsonPropertyName("psd_window_id")] string PsdWindowId,
    [property: JsonPropertyName("rbp_window_id")] string RbpWindowId,
    [property: JsonPropertyName("project_id")] string? ProjectId = null);

public sealed record CustomFrequencyRangeRequest(
    [property: JsonPropertyName("psd_window_id")] string PsdWindowId,
    [property: JsonPropertyName("rbp_window_id")] string RbpWindowId,
    [property: JsonPropertyName("low_hz")] double LowHz,
    [property: JsonPropertyName("high_hz")] double HighHz,
    [property: JsonPropertyName("project_id")] string? ProjectId = null);

public sealed record CustomFrequencyProvenance(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("scientific_version")] string ScientificVersion,
    [property: JsonPropertyName("implementation_identity")] string ImplementationIdentity,
    [property: JsonPropertyName("configuration_sha256")] string ConfigurationSha256,
    [property: JsonPropertyName("window_policy_version")] string WindowPolicyVersion);

public sealed record CustomFrequencyBinInterval(
    [property: JsonPropertyName("low_hz")] double LowHz,
    [property: JsonPropertyName("high_hz")] double HighHz);

public sealed record CustomFrequencyBinChannel(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("psd_quality")] string PsdQuality,
    [property: JsonPropertyName("psd_quality_reasons")] IReadOnlyList<string> PsdQualityReasons,
    [property: JsonPropertyName("rbp_quality")] string RbpQuality,
    [property: JsonPropertyName("rbp_quality_reasons")] IReadOnlyList<string> RbpQualityReasons,
    [property: JsonPropertyName("rbp_denominator_uv2")] double? RbpDenominatorUv2,
    [property: JsonPropertyName("psd_density_uv2_per_hz")] IReadOnlyList<double?> PsdDensityUv2PerHz,
    [property: JsonPropertyName("rbp_absolute_power_uv2")] IReadOnlyList<double?> RbpAbsolutePowerUv2,
    [property: JsonPropertyName("rbp_relative_power_percent")] IReadOnlyList<double?> RbpRelativePowerPercent,
    [property: JsonPropertyName("amplitude_rms_uv")] IReadOnlyList<double?> AmplitudeRmsUv);

public sealed record CustomFrequencyBinsResponse(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("frequency_hz")] IReadOnlyList<double> FrequencyHz,
    [property: JsonPropertyName("bin_intervals")] IReadOnlyList<CustomFrequencyBinInterval> BinIntervals,
    [property: JsonPropertyName("channels")] IReadOnlyList<CustomFrequencyBinChannel> Channels,
    [property: JsonPropertyName("psd_provenance")] CustomFrequencyProvenance PsdProvenance,
    [property: JsonPropertyName("rbp_provenance")] CustomFrequencyProvenance RbpProvenance);

public sealed record CustomFrequencyRangeChannel(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("psd_quality")] string PsdQuality,
    [property: JsonPropertyName("psd_quality_reasons")] IReadOnlyList<string> PsdQualityReasons,
    [property: JsonPropertyName("rbp_quality")] string RbpQuality,
    [property: JsonPropertyName("rbp_quality_reasons")] IReadOnlyList<string> RbpQualityReasons,
    [property: JsonPropertyName("rbp_denominator_uv2")] double? RbpDenominatorUv2,
    [property: JsonPropertyName("psd_density_uv2_per_hz")] double? PsdDensityUv2PerHz,
    [property: JsonPropertyName("rbp_absolute_power_uv2")] double? RbpAbsolutePowerUv2,
    [property: JsonPropertyName("rbp_relative_power_percent")] double? RbpRelativePowerPercent,
    [property: JsonPropertyName("amplitude_rms_uv")] double? AmplitudeRmsUv);

public sealed record CustomFrequencyRangeResponse(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("start_sample")] long StartSample,
    [property: JsonPropertyName("end_sample")] long EndSample,
    [property: JsonPropertyName("low_hz")] double LowHz,
    [property: JsonPropertyName("high_hz")] double HighHz,
    [property: JsonPropertyName("channels")] IReadOnlyList<CustomFrequencyRangeChannel> Channels,
    [property: JsonPropertyName("psd_provenance")] CustomFrequencyProvenance PsdProvenance,
    [property: JsonPropertyName("rbp_provenance")] CustomFrequencyProvenance RbpProvenance);

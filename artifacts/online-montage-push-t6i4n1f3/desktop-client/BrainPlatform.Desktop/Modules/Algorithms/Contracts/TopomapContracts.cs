using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record TopomapPoint(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("electrode_label")] string? ElectrodeLabel,
    [property: JsonPropertyName("x")] double? X,
    [property: JsonPropertyName("y")] double? Y,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("quality_status")] string QualityStatus = "unknown",
    [property: JsonPropertyName("quality_reasons")] IReadOnlyList<string>? QualityReasons = null);

public sealed record TopomapGrid(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("x_min")] double XMin,
    [property: JsonPropertyName("x_max")] double XMax,
    [property: JsonPropertyName("y_min")] double YMin,
    [property: JsonPropertyName("y_max")] double YMax,
    [property: JsonPropertyName("head_radius")] double HeadRadius,
    [property: JsonPropertyName("values")] IReadOnlyList<IReadOnlyList<double?>> Values);

public sealed record TopomapField(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("frequency_hz")] double? FrequencyHz,
    [property: JsonPropertyName("points")] IReadOnlyList<TopomapPoint> Points,
    [property: JsonPropertyName("grid")] TopomapGrid? Grid,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("included_channels")] IReadOnlyList<string> IncludedChannels,
    [property: JsonPropertyName("excluded_channels")] IReadOnlyList<string> ExcludedChannels,
    [property: JsonPropertyName("failure")] JsonElement? Failure,
    [property: JsonPropertyName("provenance")] IReadOnlyDictionary<string, JsonElement>? Provenance,
    [property: JsonPropertyName("frequency_range_hz")] IReadOnlyList<double>? FrequencyRangeHz = null);

public sealed record TopomapResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("window_start_s")] double WindowStartSeconds,
    [property: JsonPropertyName("window_end_s")] double WindowEndSeconds,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("algorithm_version")] string AlgorithmVersion,
    [property: JsonPropertyName("implementation_version")] string ImplementationVersion,
    [property: JsonPropertyName("coordinate_source")] string CoordinateSource,
    [property: JsonPropertyName("coordinate_frame")] string CoordinateFrame,
    [property: JsonPropertyName("coordinate_unit")] string CoordinateUnit,
    [property: JsonPropertyName("fields")] IReadOnlyList<TopomapField> Fields,
    [property: JsonPropertyName("failure")] JsonElement? Failure,
    [property: JsonPropertyName("provenance")] IReadOnlyDictionary<string, JsonElement>? Provenance);

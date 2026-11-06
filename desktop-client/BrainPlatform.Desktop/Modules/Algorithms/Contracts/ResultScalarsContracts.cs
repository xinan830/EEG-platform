using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record ResultScalarPoint(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("quality_status")] string QualityStatus,
    [property: JsonPropertyName("quality_reasons")] IReadOnlyList<string> QualityReasons);

public sealed record ResultScalarField(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("points")] IReadOnlyList<ResultScalarPoint> Points,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("failure")] JsonElement? Failure,
    [property: JsonPropertyName("provenance")] IReadOnlyDictionary<string, JsonElement>? Provenance);

public sealed record ResultScalarsResponse(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("algorithm_id")] string AlgorithmId,
    [property: JsonPropertyName("algorithm_version")] string AlgorithmVersion,
    [property: JsonPropertyName("implementation_version")] string ImplementationVersion,
    [property: JsonPropertyName("window_start_s")] double WindowStartSeconds,
    [property: JsonPropertyName("window_end_s")] double WindowEndSeconds,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("fields")] IReadOnlyList<ResultScalarField> Fields,
    [property: JsonPropertyName("failure")] JsonElement? Failure);

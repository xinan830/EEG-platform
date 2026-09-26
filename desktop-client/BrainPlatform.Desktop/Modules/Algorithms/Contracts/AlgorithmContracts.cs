using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record AlgorithmCatalogResponse(
    [property: JsonPropertyName("algorithms")] IReadOnlyList<AlgorithmCatalogItem> Algorithms);

public sealed record AlgorithmCatalogItem(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("display_name_zh")] string DisplayNameZh,
    [property: JsonPropertyName("abbreviation")] string Abbreviation,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] IReadOnlyList<AlgorithmParameter> Parameters,
    [property: JsonPropertyName("modes")] IReadOnlyList<string> Modes,
    [property: JsonPropertyName("output_schema")] JsonElement OutputSchema,
    [property: JsonPropertyName("dynamic_policy")] DynamicAnalysisPolicy DynamicPolicy,
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("is_runnable")] bool IsRunnable,
    [property: JsonPropertyName("definition_id")] string? DefinitionId,
    [property: JsonPropertyName("definition_version")] string? DefinitionVersion,
    [property: JsonPropertyName("implementation_identity")] string? ImplementationIdentity)
{
    public string ModesDisplay =>
        Modes.Count == 0 ? "未声明" : string.Join("、", Modes.Select(FormatMode));

    public string AvailabilityDisplay => Availability switch
    {
        "available" when IsRunnable => "可运行",
        "available" => "可用但不可运行",
        "shadow_validation" => "验证中",
        "unavailable" => "不可用",
        _ => Availability,
    };

    private static string FormatMode(string mode) => mode switch
    {
        "static" => "静态",
        "dynamic" => "动态",
        _ => mode,
    };
}

public sealed record AlgorithmParameter(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label_zh")] string LabelZh,
    [property: JsonPropertyName("value_type")] string ValueType,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("default")] JsonElement? Default,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("minimum")] double? Minimum,
    [property: JsonPropertyName("maximum")] double? Maximum,
    [property: JsonPropertyName("step")] double? Step,
    [property: JsonPropertyName("affects_science")] bool AffectsScience,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("options")] IReadOnlyList<AlgorithmParameterOption> Options,
    [property: JsonPropertyName("description_zh")] string DescriptionZh);

public sealed record AlgorithmParameterOption(
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("label_zh")] string LabelZh);

public sealed record DynamicAnalysisPolicy(
    [property: JsonPropertyName("minimum_window_s")] double MinimumWindowSeconds,
    [property: JsonPropertyName("window_options_s")] IReadOnlyList<double> WindowOptionsSeconds,
    [property: JsonPropertyName("default_window_s")] double DefaultWindowSeconds,
    [property: JsonPropertyName("refresh_step_s")] double RefreshStepSeconds,
    [property: JsonPropertyName("allow_warmup")] bool AllowWarmup);

public sealed record AnalysisRunRequest(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("analysis_type")] string AnalysisType,
    [property: JsonPropertyName("config")] JsonElement Config);

public sealed record AnalysisRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("analysis_type")] string AnalysisType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scientific_version")] string? ScientificVersion,
    [property: JsonPropertyName("requested_range")] TimeRange? RequestedRange,
    [property: JsonPropertyName("actual_range")] TimeRange? ActualRange,
    [property: JsonPropertyName("result_summary")] JsonElement? ResultSummary,
    [property: JsonPropertyName("analysis_provenance")] JsonElement? AnalysisProvenance,
    [property: JsonPropertyName("error")] StructuredRunError? Error,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record TimeRange(
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds);

public sealed record StructuredRunError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("details")] JsonElement? Details);

public sealed record RunArtifact(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("media_type")] string MediaType,
    [property: JsonPropertyName("byte_size")] long ByteSize,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("shape")] IReadOnlyDictionary<string, int[]> Shape,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

// This mirrors ResultService.structured_preview: the backend owns all axes,
// units, values, and window states; the desktop only renders the bounded view.
public sealed record StructuredPreviewResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("artifact")] RunArtifact Artifact,
    [property: JsonPropertyName("output")] JsonElement? Output,
    [property: JsonPropertyName("channel_order")] IReadOnlyList<string> ChannelOrder,
    [property: JsonPropertyName("requested_range")] TimeRange? RequestedRange,
    [property: JsonPropertyName("actual_range")] TimeRange? ActualRange,
    [property: JsonPropertyName("axes")] IReadOnlyDictionary<string, JsonElement> Axes,
    [property: JsonPropertyName("axis_metadata")] IReadOnlyDictionary<string, JsonElement> AxisMetadata,
    [property: JsonPropertyName("arrays")] IReadOnlyDictionary<string, JsonElement> Arrays,
    [property: JsonPropertyName("array_metadata")] IReadOnlyDictionary<string, JsonElement> ArrayMetadata,
    [property: JsonPropertyName("windows")] IReadOnlyList<JsonElement> Windows,
    [property: JsonPropertyName("window_state_counts")] IReadOnlyDictionary<string, int> WindowStateCounts,
    [property: JsonPropertyName("quality")] JsonElement? Quality,
    [property: JsonPropertyName("scientific_version")] string? ScientificVersion,
    [property: JsonPropertyName("implementation_version")] string? ImplementationVersion);

public sealed class AlgorithmApiException : Exception
{
    public AlgorithmApiException(HttpStatusCode statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public string Code { get; }
}

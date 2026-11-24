using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record Topomap3DLocationRequest(
    [property: JsonPropertyName("channels")] IReadOnlyList<string> Channels);

public sealed record Topomap3DLocationResponse(
    [property: JsonPropertyName("mapping_id")] string MappingId,
    [property: JsonPropertyName("mapping_version")] int MappingVersion,
    [property: JsonPropertyName("mapping_sha256")] string MappingSha256,
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("model_version")] string ModelVersion,
    [property: JsonPropertyName("source_montage")] string SourceMontage,
    [property: JsonPropertyName("source_coordinate_frame")] string SourceCoordinateFrame,
    [property: JsonPropertyName("source_coordinate_unit")] string SourceCoordinateUnit,
    [property: JsonPropertyName("target_coordinate_frame")] string TargetCoordinateFrame,
    [property: JsonPropertyName("target_coordinate_unit")] string TargetCoordinateUnit,
    [property: JsonPropertyName("locations")] IReadOnlyList<Topomap3DResolvedLocation> Locations);

public sealed record Topomap3DResolvedLocation(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("canonical_channel")] string? CanonicalChannel,
    [property: JsonPropertyName("source_coordinate_mne")] IReadOnlyList<double>? SourceCoordinateMne,
    [property: JsonPropertyName("target_coordinate_gnm")] IReadOnlyList<double>? TargetCoordinateGnm,
    [property: JsonPropertyName("mapping_method")] string? MappingMethod,
    [property: JsonPropertyName("is_scalp_field_site")] bool IsScalpFieldSite,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason);

using System.Text.Json;
using System.Text.Json.Serialization;
using BrainPlatform.Desktop.Modules.Channels.Api;

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

public sealed record Topomap3DElectrode(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("electrode_label")] string ElectrodeLabel,
    [property: JsonPropertyName("coordinate_gnm")] IReadOnlyList<double> CoordinateGnm,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("mapping_method")] string MappingMethod,
    [property: JsonPropertyName("accuracy_warning")] string? AccuracyWarning);

public sealed record Topomap3DExcludedChannel(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("electrode_label")] string? ElectrodeLabel,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record Topomap3DBand(
    [property: JsonPropertyName("band_id")] string BandId,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("vertex_values")] IReadOnlyList<double?> VertexValues,
    [property: JsonPropertyName("electrodes")] IReadOnlyList<Topomap3DElectrode> Electrodes,
    [property: JsonPropertyName("included_channels")] IReadOnlyList<string> IncludedChannels,
    [property: JsonPropertyName("excluded_channels")] IReadOnlyList<Topomap3DExcludedChannel> ExcludedChannels,
    [property: JsonPropertyName("value_min")] double? ValueMin,
    [property: JsonPropertyName("value_max")] double? ValueMax,
    [property: JsonPropertyName("failure")] JsonElement? Failure);

public sealed record Topomap3DResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("window_start_s")] double WindowStartSeconds,
    [property: JsonPropertyName("window_end_s")] double WindowEndSeconds,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("algorithm_id")] string AlgorithmId,
    [property: JsonPropertyName("algorithm_version")] string AlgorithmVersion,
    [property: JsonPropertyName("implementation_version")] string ImplementationVersion,
    [property: JsonPropertyName("power_type")] string PowerType,
    [property: JsonPropertyName("quality")] string? Quality,
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("model_version")] string ModelVersion,
    [property: JsonPropertyName("mapping_id")] string MappingId,
    [property: JsonPropertyName("mapping_version")] int MappingVersion,
    [property: JsonPropertyName("mapping_sha256")] string MappingSha256,
    [property: JsonPropertyName("mesh_asset_sha256")] string MeshAssetSha256,
    [property: JsonPropertyName("mesh_geometry_sha256")] string MeshGeometrySha256,
    [property: JsonPropertyName("source_montage")] string SourceMontage,
    [property: JsonPropertyName("source_mne_version")] string SourceMneVersion,
    [property: JsonPropertyName("source_coordinate_frame")] string SourceCoordinateFrame,
    [property: JsonPropertyName("source_coordinate_unit")] string SourceCoordinateUnit,
    [property: JsonPropertyName("target_coordinate_frame")] string TargetCoordinateFrame,
    [property: JsonPropertyName("target_coordinate_unit")] string TargetCoordinateUnit,
    [property: JsonPropertyName("spatial_method")] string SpatialMethod,
    [property: JsonPropertyName("spatial_parameters")] IReadOnlyDictionary<string, JsonElement> SpatialParameters,
    [property: JsonPropertyName("intended_use")] string IntendedUse,
    [property: JsonPropertyName("accuracy_status")] string AccuracyStatus,
    [property: JsonPropertyName("bands")] IReadOnlyList<Topomap3DBand> Bands,
    [property: JsonPropertyName("failure")] JsonElement? Failure,
    [property: JsonPropertyName("provenance")] IReadOnlyDictionary<string, JsonElement>? Provenance);

public sealed record Topomap3DPresentation(ElectrodeScalpModel3D Model, Topomap3DResponse Result);

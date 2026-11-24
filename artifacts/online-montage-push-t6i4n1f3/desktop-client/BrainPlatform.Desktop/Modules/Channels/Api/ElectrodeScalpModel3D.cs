using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Channels.Api;

public sealed record ElectrodeLocation3D(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("canonical_channel")] string CanonicalChannel,
    [property: JsonPropertyName("coordinate_gnm")] IReadOnlyList<double> CoordinateGnm,
    [property: JsonPropertyName("mapping_method")] string MappingMethod,
    [property: JsonPropertyName("manual_adjusted")] bool ManualAdjusted,
    [property: JsonPropertyName("is_scalp_field_site")] bool IsScalpFieldSite);

public sealed record ElectrodeScalpModel3D(
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
    [property: JsonPropertyName("intended_use")] string IntendedUse,
    [property: JsonPropertyName("accuracy_status")] string AccuracyStatus,
    [property: JsonPropertyName("head_origin_gnm")] IReadOnlyList<double> HeadOriginGnm,
    [property: JsonPropertyName("right_axis_gnm")] IReadOnlyList<double> RightAxisGnm,
    [property: JsonPropertyName("forward_axis_gnm")] IReadOnlyList<double> ForwardAxisGnm,
    [property: JsonPropertyName("up_axis_gnm")] IReadOnlyList<double> UpAxisGnm,
    [property: JsonPropertyName("electrode_locations")] IReadOnlyList<ElectrodeLocation3D> ElectrodeLocations,
    [property: JsonPropertyName("vertices_gnm")] IReadOnlyList<IReadOnlyList<double>> VerticesGnm,
    [property: JsonPropertyName("triangles")] IReadOnlyList<IReadOnlyList<int>> Triangles);

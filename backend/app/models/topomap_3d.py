"""Typed contracts for the standard-template GNM 3D topomap."""

from typing import Literal

from pydantic import BaseModel, Field


class Topomap3DModelResponse(BaseModel):
    model_id: str
    model_version: str
    mapping_id: str
    mapping_version: int
    mapping_sha256: str
    mesh_asset_sha256: str
    mesh_geometry_sha256: str
    source_montage: str
    source_mne_version: str
    source_coordinate_frame: Literal["head"]
    source_coordinate_unit: Literal["m"]
    target_coordinate_frame: Literal["gnm_native"]
    target_coordinate_unit: Literal["GNM model units"]
    intended_use: Literal["standard_template_visualization"]
    accuracy_status: str
    head_origin_gnm: tuple[float, float, float]
    right_axis_gnm: tuple[float, float, float]
    forward_axis_gnm: tuple[float, float, float]
    up_axis_gnm: tuple[float, float, float]
    electrode_locations: list["Topomap3DLocation"]
    vertices_gnm: list[tuple[float, float, float]]
    triangles: list[tuple[int, int, int]]


class Topomap3DLocation(BaseModel):
    channel: str
    canonical_channel: str
    coordinate_gnm: tuple[float, float, float]
    mapping_method: str
    manual_adjusted: bool
    is_scalp_field_site: bool


class Topomap3DLocationRequest(BaseModel):
    channels: list[str] = Field(min_length=1, max_length=256)


class Topomap3DResolvedLocation(BaseModel):
    channel: str
    canonical_channel: str | None = None
    source_coordinate_mne: tuple[float, float, float] | None = None
    target_coordinate_gnm: tuple[float, float, float] | None = None
    mapping_method: str | None = None
    is_scalp_field_site: bool
    status: Literal["available", "unavailable"]
    reason: str | None = None


class Topomap3DLocationResponse(BaseModel):
    mapping_id: str
    mapping_version: int
    mapping_sha256: str
    model_id: str
    model_version: str
    source_montage: str
    source_coordinate_frame: Literal["head"]
    source_coordinate_unit: Literal["m"]
    target_coordinate_frame: Literal["gnm_native"]
    target_coordinate_unit: Literal["GNM model units"]
    locations: list[Topomap3DResolvedLocation]


class Topomap3DElectrode(BaseModel):
    channel: str
    electrode_label: str
    coordinate_gnm: tuple[float, float, float]
    value: float
    mapping_method: str
    accuracy_warning: str | None = None


class Topomap3DExcludedChannel(BaseModel):
    channel: str
    electrode_label: str | None = None
    reason: str


class Topomap3DBand(BaseModel):
    band_id: Literal["delta", "theta", "alpha", "beta", "gamma"]
    unit: Literal["ratio", "uV^2"]
    status: Literal["ready", "unavailable"]
    vertex_values: list[float | None] = Field(default_factory=list)
    electrodes: list[Topomap3DElectrode] = Field(default_factory=list)
    included_channels: list[str] = Field(default_factory=list)
    excluded_channels: list[Topomap3DExcludedChannel] = Field(default_factory=list)
    value_min: float | None = None
    value_max: float | None = None
    failure: dict[str, object] | None = None


class Topomap3DResponse(BaseModel):
    run_id: str
    recording_id: str
    window_start_s: float = Field(ge=0)
    window_end_s: float = Field(ge=0)
    status: Literal["ready", "unavailable"]
    algorithm_id: Literal["rbp"]
    algorithm_version: str
    implementation_version: str
    power_type: Literal["relative", "absolute"]
    quality: str | None = None
    model_id: str
    model_version: str
    mapping_id: str
    mapping_version: int
    mapping_sha256: str
    mesh_asset_sha256: str
    mesh_geometry_sha256: str
    source_montage: str
    source_mne_version: str
    source_coordinate_frame: Literal["head"]
    source_coordinate_unit: Literal["m"]
    target_coordinate_frame: Literal["gnm_native"]
    target_coordinate_unit: Literal["GNM model units"]
    spatial_method: Literal["gnm_upper_scalp_gnomonic_linear_v1"]
    spatial_parameters: dict[str, object]
    intended_use: Literal["standard_template_visualization"]
    accuracy_status: str
    bands: list[Topomap3DBand] = Field(default_factory=list)
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)

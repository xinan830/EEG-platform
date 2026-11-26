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


class Topomap3DMontageSummary(BaseModel):
    name: str
    display_name: str
    category: str
    channel_count: int
    is_default: bool


class Topomap3DMontageCatalogResponse(BaseModel):
    default_montage: str
    montages: list[Topomap3DMontageSummary]


class Topomap3DMontageSite(BaseModel):
    channel: str
    canonical_channel: str
    map_x: float
    map_y: float
    coordinate_gnm: tuple[float, float, float]
    is_scalp_field_site: bool


class Topomap3DMontagePreviewResponse(BaseModel):
    montage_name: str
    display_name: str
    is_default: bool
    channel_count: int
    projection_source: Literal["gnm_mapped_colin27", "dynamic_head_mesh_projection"]
    sites: list[Topomap3DMontageSite]

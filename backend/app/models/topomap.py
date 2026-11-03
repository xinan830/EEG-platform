"""Typed contract for backend-produced EEG topomap payloads."""

from typing import Literal

from pydantic import BaseModel, Field


TopomapStatus = Literal["ready", "unavailable"]
TopomapPointStatus = Literal["included", "excluded"]
TopomapPowerType = Literal["relative", "absolute"]


class TopomapPoint(BaseModel):
    channel: str
    electrode_label: str | None = None
    x: float | None = None
    y: float | None = None
    value: float | None = None
    status: TopomapPointStatus
    reason: str | None = None


class TopomapGrid(BaseModel):
    method: Literal["inverse_distance_weighted_v1"]
    width: int = Field(gt=0)
    height: int = Field(gt=0)
    x_min: float
    x_max: float
    y_min: float
    y_max: float
    head_radius: float = Field(gt=0, le=1)
    values: list[list[float | None]]


class TopomapBand(BaseModel):
    band_id: str
    unit: str
    points: list[TopomapPoint]
    grid: TopomapGrid | None = None
    status: TopomapStatus
    included_channels: list[str] = Field(default_factory=list)
    excluded_channels: list[str] = Field(default_factory=list)
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)


class TopomapResponse(BaseModel):
    run_id: str
    recording_id: str
    window_start_s: float = Field(ge=0)
    window_end_s: float = Field(gt=0)
    status: TopomapStatus
    algorithm_version: str
    implementation_version: str
    coordinate_source: Literal["montage_position_snapshot"]
    coordinate_frame: Literal["mri"]
    coordinate_unit: Literal["m"]
    power_type: TopomapPowerType = "relative"
    bands: list[TopomapBand] = Field(default_factory=list)
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)

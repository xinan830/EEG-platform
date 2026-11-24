"""Typed contract for backend-produced EEG topomap payloads."""

from typing import Literal

from pydantic import BaseModel, Field


TopomapStatus = Literal["ready", "unavailable"]
TopomapPointStatus = Literal["included", "excluded"]


class TopomapPoint(BaseModel):
    channel: str
    electrode_label: str | None = None
    x: float | None = None
    y: float | None = None
    value: float | None = None
    status: TopomapPointStatus
    reason: str | None = None
    quality_status: str = "unknown"
    quality_reasons: list[str] = Field(default_factory=list)


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


class TopomapField(BaseModel):
    field_id: str = Field(min_length=1)
    label: str = Field(min_length=1)
    unit: str
    frequency_hz: float | None = None
    frequency_range_hz: tuple[float, float] | None = None
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
    fields: list[TopomapField] = Field(default_factory=list)
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)

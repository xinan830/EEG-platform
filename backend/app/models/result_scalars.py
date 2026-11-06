"""Non-spatial scalar projections of a saved analysis window."""

from typing import Literal

from pydantic import BaseModel, Field


class ResultScalarPoint(BaseModel):
    channel: str
    value: float | None
    quality_status: str
    quality_reasons: list[str] = Field(default_factory=list)


class ResultScalarField(BaseModel):
    field_id: str
    label: str
    unit: str
    points: list[ResultScalarPoint]
    status: Literal["ready", "unavailable"]
    failure: dict[str, object] | None = None
    provenance: dict[str, object] = Field(default_factory=dict)


class ResultScalarsResponse(BaseModel):
    window_id: str
    recording_id: str
    algorithm_id: str
    algorithm_version: str
    implementation_version: str
    window_start_s: float = Field(ge=0)
    window_end_s: float = Field(gt=0)
    status: Literal["ready", "unavailable"]
    fields: list[ResultScalarField] = Field(default_factory=list)
    failure: dict[str, object] | None = None

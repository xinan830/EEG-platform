"""Typed contracts shared by algorithm modules and the Run service."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

from .parameter_schema import AlgorithmParameter, ParameterSchema


AlgorithmMode = Literal["static", "dynamic"]


class AlgorithmFailure(BaseModel):
    code: str = Field(min_length=1)
    message: str = Field(min_length=1)
    detail: dict[str, Any] = Field(default_factory=dict)


class AlgorithmConfigBase(BaseModel):
    model_config = ConfigDict(extra="forbid")

    channel: str = Field(min_length=1)
    mode: AlgorithmMode
    start_s: float = Field(ge=0)
    end_s: float = Field(gt=0)
    window_s: float | None = Field(default=None, gt=0)
    step_s: float | None = Field(default=None, gt=0)


class AlgorithmManifest(BaseModel):
    algorithm_id: str = Field(min_length=1)
    display_name_zh: str = Field(min_length=1)
    abbreviation: str = Field(min_length=1)
    purpose_zh: str = Field(min_length=1)
    scientific_version: str = Field(min_length=1)
    implementation_identity: str = Field(min_length=1)
    supported_modes: list[AlgorithmMode] = Field(default_factory=lambda: ["static"])
    output_unit: str = Field(min_length=1)


class AlgorithmInputs(BaseModel):
    recording_id: str = Field(min_length=1)
    channel: str = Field(min_length=1)
    sfreq_hz: float = Field(gt=0)
    duration_s: float = Field(gt=0)
    payload: Any


class AlgorithmResult(BaseModel):
    value: float | None = None
    unit: str
    channel: str
    requested_range: dict[str, float]
    actual_range: dict[str, float] | None = None
    quality: str
    failure: AlgorithmFailure | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)


class AlgorithmSeriesResult(BaseModel):
    values: list[float | None]
    time_centers_s: list[float]
    unit: str
    channel: str
    windows: list[dict[str, float]]
    quality: list[str]
    failures: list[AlgorithmFailure | None]
    evidence: dict[str, Any] = Field(default_factory=dict)


class ExecutionContext(BaseModel):
    recording_id: str = Field(min_length=1)
    config: dict[str, Any] = Field(default_factory=dict)
    recording: Any


class AlgorithmModuleContract(BaseModel):
    """Serializable portion of a module used by the catalog."""

    manifest: AlgorithmManifest
    parameters: list[AlgorithmParameter] = Field(default_factory=list)
    output_schema: dict[str, Any] = Field(default_factory=dict)

    @property
    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=self.parameters)

"""Typed contracts shared by algorithm modules and the Run service."""

from __future__ import annotations

from typing import Any, Literal, Protocol, runtime_checkable

from pydantic import BaseModel, ConfigDict, Field, model_validator

from .parameter_schema import AlgorithmParameter, ParameterSchema


AlgorithmMode = Literal["static", "dynamic"]

# Persisted dynamic points carry this independently from an algorithm's
# scientific version.  Altering point timing or evidence requires a new value
# so older cached summaries cannot be rendered as the current contract.
DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION = "dynamic-analysis-frame-v3"


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

    @model_validator(mode="after")
    def validate_range(self) -> "AlgorithmConfigBase":
        if self.end_s <= self.start_s:
            raise ValueError("analysis range must satisfy end_s > start_s")
        if self.mode == "dynamic" and (self.window_s is None or self.step_s is None):
            raise ValueError("dynamic analysis requires window_s and step_s")
        return self


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
    time_s: list[float]
    unit: str
    channel: str
    windows: list[dict[str, float]]
    quality: list[str]
    failures: list[AlgorithmFailure | None]
    warmups: list[bool] = Field(default_factory=list)
    point_evidence: list[dict[str, Any]] = Field(default_factory=list)
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


@runtime_checkable
class AlgorithmModule(Protocol):
    """Runtime-facing behavior implemented by each algorithm package."""

    manifest: AlgorithmManifest
    config_model: type[AlgorithmConfigBase]

    def parameter_schema(self) -> ParameterSchema: ...

    def resolve_inputs(self, recording: Any, config: AlgorithmConfigBase) -> AlgorithmInputs: ...

    def execute_static(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmResult: ...

    def execute_dynamic(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmSeriesResult: ...

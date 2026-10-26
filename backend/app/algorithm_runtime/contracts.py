"""Typed contracts shared by algorithm modules and the Run service."""

from __future__ import annotations

from typing import Any, Literal, Protocol, runtime_checkable

import numpy as np
from pydantic import BaseModel, ConfigDict, Field, model_validator

from .parameter_schema import AlgorithmParameter, ParameterSchema
from .execution_control import ExecutionControl


AlgorithmMode = Literal["static", "dynamic"]
StructuredOutputKind = Literal["frequency_series", "time_frequency", "algorithm_group"]
DynamicStructuredOutputKind = Literal["frequency_series", "time_frequency", "algorithm_group"]
CapabilityImplementationStatus = Literal["implemented", "unsupported"]
CapabilityValidationStatus = Literal["validated", "unvalidated", "not_applicable"]

# Persisted dynamic points carry this independently from an algorithm's
# scientific version.  Altering point timing or evidence requires a new value
# so older cached summaries cannot be rendered as the current contract.
OFFICIAL_ALGORITHM_RESULT_CONTRACT_VERSION = "official-algorithm-result-v1"
DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION = "dynamic-analysis-frame-v6"
DynamicAnalysisState = Literal["Partial", "Complete", "Rejected", "Unavailable"]


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


class DynamicAnalysisPolicy(BaseModel):
    """Algorithm-owned scheduling rules, separate from EEG mathematics."""

    minimum_window_s: float = Field(default=4.0, gt=0)
    window_options_s: list[float] = Field(default_factory=lambda: [5.0, 10.0, 20.0, 30.0])
    default_window_s: float = Field(default=5.0, gt=0)
    refresh_step_s: float = Field(default=1.0, gt=0)
    refresh_step_options_s: list[float] = Field(default_factory=lambda: [1.0, 2.0, 5.0])
    allow_warmup: bool = True

    @model_validator(mode="after")
    def validate_options(self) -> "DynamicAnalysisPolicy":
        if not self.window_options_s or any(value <= 0 for value in self.window_options_s):
            raise ValueError("dynamic window options must contain positive values")
        if self.default_window_s not in self.window_options_s:
            raise ValueError("dynamic default window must be one of the supported options")
        if self.default_window_s < self.minimum_window_s:
            raise ValueError("dynamic default window cannot be shorter than its minimum")
        if not self.refresh_step_options_s or any(value <= 0 for value in self.refresh_step_options_s):
            raise ValueError("dynamic refresh step options must contain positive values")
        if self.refresh_step_s not in self.refresh_step_options_s:
            raise ValueError("dynamic default refresh step must be one of the supported options")
        return self


class AlgorithmExecutionSnapshot(BaseModel):
    """Immutable execution semantics supplied by the selected module."""

    window: dict[str, Any] = Field(default_factory=dict)
    filters: dict[str, Any] = Field(default_factory=dict)
    quality_rules: dict[str, Any] = Field(default_factory=dict)


class AlgorithmEvidence(BaseModel):
    """Validated common evidence plus named algorithm-owned extensions."""

    model_config = ConfigDict(extra="forbid")

    source_quality: dict[str, Any] = Field(default_factory=dict)
    spectral_evidence: dict[str, Any] = Field(default_factory=dict)
    calculation_trace: dict[str, Any] = Field(default_factory=dict)
    extensions: dict[str, dict[str, Any]] = Field(default_factory=dict)


class DefinitionPersistenceMetadata(BaseModel):
    """Identity used only to persist the immutable Definition record."""

    name: str = Field(min_length=1)
    version: str = Field(min_length=1)
    definition_metadata: dict[str, Any] = Field(default_factory=dict)
    graph: dict[str, Any] = Field(default_factory=dict)
    inputs: dict[str, Any] = Field(default_factory=dict)
    outputs: dict[str, Any] = Field(default_factory=dict)
    quality_rules: dict[str, Any] = Field(default_factory=dict)
    references: list[str] = Field(default_factory=list)


class CapabilityEvidenceStatus(BaseModel):
    implementation: CapabilityImplementationStatus
    validation: CapabilityValidationStatus
    evidence: list[str] = Field(default_factory=list)
    reason: str | None = None

    @model_validator(mode="after")
    def validate_evidence_state(self) -> "CapabilityEvidenceStatus":
        if self.implementation == "unsupported" and not self.reason:
            raise ValueError("unsupported capability requires a reason")
        if self.validation == "validated" and not self.evidence:
            raise ValueError("validated capability requires evidence references")
        return self


class DynamicStrategyCapabilities(BaseModel):
    full: CapabilityEvidenceStatus
    on_demand: CapabilityEvidenceStatus
    combined_preview_background_full: CapabilityEvidenceStatus
    full_result_to_preview: CapabilityEvidenceStatus
    on_demand_result_to_full: CapabilityEvidenceStatus


class SignalRequirements(BaseModel):
    """Module-owned declaration of source roles and preprocessing scope."""

    input_roles: list[str] = Field(min_length=1)
    representation: Literal["time_series", "spectrum", "spectrogram"]
    source_shape: Literal["single", "paired"]
    preprocessing_scope: Literal[
        "configured_continuous_before_slice",
        "fixed_continuous_before_slice",
        "module_defined",
    ]
    missing_sample_policy: Literal["preserve_nan", "reject"] = "preserve_nan"


class AlgorithmOutputField(BaseModel):
    """A public algorithm output with explicit units and array shape."""

    name: str = Field(min_length=1)
    unit: str = Field(min_length=1)
    meaning: str = Field(min_length=1)
    shape: list[str | int] = Field(default_factory=list)


class AlgorithmOutputContract(BaseModel):
    """Module-owned declaration consumed by runtime validation and catalogs."""

    static_result_type: Literal["scalar", "structured"]
    dynamic_result_type: Literal["series", "structured_series"]
    channel_role_order: list[str] = Field(min_length=1)
    fields: list[AlgorithmOutputField] = Field(min_length=1)
    axes: list[dict[str, Any]] = Field(default_factory=list)
    quality_values: list[str] = Field(min_length=1)
    dynamic_states: list[DynamicAnalysisState] = Field(default_factory=list)
    failure_semantics: str = Field(min_length=1)
    evidence_fields: list[str] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_declarations(self) -> "AlgorithmOutputContract":
        names = [field.name for field in self.fields]
        if len(names) != len(set(names)):
            raise ValueError("algorithm output field names must be unique")
        if len(self.channel_role_order) != len(set(self.channel_role_order)):
            raise ValueError("algorithm output channel role order must be unique")
        return self


class AlgorithmManifest(BaseModel):
    algorithm_id: str = Field(min_length=1)
    display_name_zh: str = Field(min_length=1)
    abbreviation: str = Field(min_length=1)
    purpose_zh: str = Field(min_length=1)
    scientific_version: str = Field(min_length=1)
    # Official modules must opt in explicitly. Generic/user modules retain
    # their registry's existing version semantics and ignore this marker.
    is_current_version: bool = False
    implementation_identity: str = Field(min_length=1)
    supported_modes: list[AlgorithmMode] = Field(default_factory=lambda: ["static"])
    output_schema: dict[str, Any] = Field(min_length=1)
    output_contract: AlgorithmOutputContract | None = None
    dynamic_policy: DynamicAnalysisPolicy = Field(default_factory=DynamicAnalysisPolicy)
    dynamic_capabilities: DynamicStrategyCapabilities | None = None
    # This nested record is persistence provenance, not scientific metadata.
    # Official modules declare it; user modules leave it absent.
    definition_persistence: DefinitionPersistenceMetadata | None = None
    execution_kind: str = "runtime_algorithm"
    availability: Literal["shadow_validation", "available", "deprecated"] = "available"
    is_runnable: bool = True
    required_channel_roles: list[str] = Field(default_factory=list)
    signal_requirements: SignalRequirements | None = None
    catalog_group: str | None = None


class AlgorithmInputs(BaseModel):
    model_config = ConfigDict(arbitrary_types_allowed=True)

    recording_id: str = Field(min_length=1)
    channel: str = Field(min_length=1)
    sfreq_hz: float = Field(gt=0)
    duration_s: float = Field(gt=0)
    payload: Any
    execution_context: ExecutionControl | None = Field(default=None, exclude=True)


class AlgorithmResult(BaseModel):
    value: float | None = None
    unit: str
    channel: str
    requested_range: dict[str, float]
    actual_range: dict[str, float] | None = None
    quality: str
    failure: AlgorithmFailure | None = None
    output_values: dict[str, float | None] | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)


class AlgorithmStructuredResult(BaseModel):
    """Contract for multi-axis scientific outputs stored as artifacts.

    Numeric arrays are carried only across the in-process execution boundary;
    the Run layer persists them as immutable NPZ arrays and keeps this
    metadata in the result summary. This prevents large matrices from being
    copied into JSON or frontend state.
    """

    model_config = ConfigDict(arbitrary_types_allowed=True)

    output_kind: StructuredOutputKind
    channel_order: list[str] = Field(min_length=1)
    axes: dict[str, Any] = Field(default_factory=dict)
    axis_units: dict[str, str] = Field(default_factory=dict)
    arrays: dict[str, Any] = Field(default_factory=dict)
    array_units: dict[str, str] = Field(default_factory=dict)
    requested_range: dict[str, float]
    actual_range: dict[str, float] | None = None
    quality: str
    failure: AlgorithmFailure | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="after")
    def validate_output(self) -> "AlgorithmStructuredResult":
        if not self.arrays:
            raise ValueError("structured algorithm result requires at least one numeric array")
        if set(self.array_units) != set(self.arrays):
            raise ValueError("structured result array units must match array names")
        if set(self.axis_units) != set(self.axes):
            raise ValueError("structured result axis units must match axis names")
        if not self.axes:
            raise ValueError("structured algorithm result requires explicit axes")
        if self.failure is None and self.quality in {"failed", "gate_failed", "unavailable"}:
            raise ValueError("failed structured result requires a failure reason")
        return self


class StructuredSeriesWindow(BaseModel):
    """One recording-relative window in a dynamic structured result."""

    start_sample: int = Field(ge=0)
    end_sample: int = Field(gt=0)
    start_s: float = Field(ge=0)
    end_s: float = Field(gt=0)
    state: DynamicAnalysisState
    quality: str = Field(min_length=1)
    failure: AlgorithmFailure | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)
    provenance: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="after")
    def validate_range(self) -> "StructuredSeriesWindow":
        if self.end_sample <= self.start_sample or self.end_s <= self.start_s:
            raise ValueError("structured series window must have a positive range")
        if self.state in {"Rejected", "Unavailable"} and self.failure is None:
            raise ValueError("rejected or unavailable window requires a failure reason")
        return self


class AlgorithmStructuredSeriesResult(BaseModel):
    """Dynamic matrix series persisted through the structured artifact path."""

    model_config = ConfigDict(arbitrary_types_allowed=True)

    output_kind: DynamicStructuredOutputKind
    channel_order: list[str] = Field(min_length=1)
    axes: dict[str, Any] = Field(default_factory=dict)
    axis_units: dict[str, str] = Field(default_factory=dict)
    arrays: dict[str, Any] = Field(default_factory=dict)
    array_units: dict[str, str] = Field(default_factory=dict)
    windows: list[StructuredSeriesWindow] = Field(min_length=1)
    requested_range: dict[str, float]
    actual_range: dict[str, float] | None = None
    quality: str = Field(min_length=1)
    failure: AlgorithmFailure | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="after")
    def validate_output(self) -> "AlgorithmStructuredSeriesResult":
        if not self.arrays:
            raise ValueError("structured series requires at least one numeric array")
        if set(self.array_units) != set(self.arrays):
            raise ValueError("structured series array units must match array names")
        if set(self.axis_units) != set(self.axes):
            raise ValueError("structured series axis units must match axis names")
        if not self.axes:
            raise ValueError("structured series requires explicit axes")
        for name, value in self.axes.items():
            axis = np.asarray(value)
            if axis.ndim != 1:
                raise ValueError(f"structured series axis {name!r} must be one-dimensional")
        for name, value in self.arrays.items():
            array = np.asarray(value)
            if array.ndim < 1 or array.shape[0] != len(self.windows):
                raise ValueError(
                    f"structured series array {name!r} must use windows as its first dimension"
                )
            if np.any(np.isinf(array)):
                raise ValueError(f"structured series array {name!r} contains infinity")
        if self.failure is None and self.quality in {"failed", "gate_failed", "unavailable"}:
            raise ValueError("failed structured series requires a failure reason")
        return self


class AlgorithmSeriesResult(BaseModel):
    model_config = ConfigDict(extra="forbid")

    values: list[float | None]
    time_s: list[float]
    unit: str
    channel: str
    windows: list[dict[str, float]]
    quality: list[str]
    failures: list[AlgorithmFailure | None]
    states: list[DynamicAnalysisState]
    point_evidence: list[dict[str, Any]] = Field(default_factory=list)
    output_values: list[dict[str, float | None]] = Field(default_factory=list)
    evidence: dict[str, Any] = Field(default_factory=dict)


class ExecutionContext(BaseModel):
    recording_id: str = Field(min_length=1)
    config: dict[str, Any] = Field(default_factory=dict)
    recording: Any


class AlgorithmModuleContract(BaseModel):
    """Serializable portion of a module used by the catalog."""

    manifest: AlgorithmManifest
    parameters: list[AlgorithmParameter] = Field(default_factory=list)
    output_schema: dict[str, Any] = Field(min_length=1)

    @property
    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=self.parameters)


@runtime_checkable
class AlgorithmModule(Protocol):
    """Runtime-facing behavior implemented by each algorithm package."""

    manifest: AlgorithmManifest
    config_model: type[AlgorithmConfigBase]

    def parameter_schema(self) -> ParameterSchema: ...

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]: ...

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot: ...

    def resolve_inputs(self, recording: Any, config: AlgorithmConfigBase) -> AlgorithmInputs: ...

    def execute_static(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmResult | AlgorithmStructuredResult | AlgorithmStructuredSeriesResult: ...

    def execute_dynamic(self, inputs: AlgorithmInputs, config: AlgorithmConfigBase) -> AlgorithmSeriesResult | AlgorithmStructuredSeriesResult: ...

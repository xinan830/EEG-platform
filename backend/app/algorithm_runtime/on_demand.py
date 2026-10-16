"""Contracts for opt-in, single-window dynamic algorithm execution.

The separate window service consumes this capability contract. Existing full
Run execution remains independent; shared scheduling never branches on science.
"""

from __future__ import annotations

import hashlib
import json
from collections.abc import Callable
from typing import Any, Literal, Protocol, runtime_checkable

import numpy as np
from pydantic import BaseModel, ConfigDict, Field, StrictInt, model_validator

from .contracts import AlgorithmConfigBase, AlgorithmInputs, AlgorithmManifest


OnDemandWindowState = Literal[
    "preparing_source",
    "queued",
    "computing_window",
    "ready",
    "insufficient_data",
    "rejected",
    "unavailable",
    "cancelled",
    "failed",
]


class DynamicSessionIdentity(BaseModel):
    """Immutable scientific identity shared by all windows in one session."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    project_scope: str | None = None
    recording_id: str = Field(min_length=1)
    source_sha256: str = Field(min_length=1)
    algorithm_id: str = Field(min_length=1)
    definition_id: str = Field(min_length=1)
    definition_version: str = Field(min_length=1)
    # Empty only for experimental historical rows whose digest/build was not
    # recorded. New executions resolve both fields from the official runtime.
    definition_sha256: str = ""
    implementation_build: str = ""
    scientific_version: str = Field(min_length=1)
    implementation_identity: str = Field(min_length=1)
    configuration_sha256: str = Field(min_length=1)
    window_policy_version: str = Field(min_length=1)


class DynamicWindowIdentity(DynamicSessionIdentity):
    """Immutable scientific identity for one recording-relative window."""

    # A retry is a new execution generation for the same scientific range.  It
    # is part of the window identity so an old cancelled/failed execution can
    # remain immutable and auditable while a retry gets its own result record.
    generation: StrictInt = Field(default=0, ge=0)
    start_sample: StrictInt = Field(ge=0)
    end_sample: StrictInt = Field(gt=0)

    @model_validator(mode="after")
    def validate_range(self) -> "DynamicWindowIdentity":
        if self.end_sample <= self.start_sample:
            raise ValueError("dynamic window must use a positive half-open sample range")
        return self


def window_identity_key(identity: DynamicWindowIdentity) -> str:
    """Build a versioned stable key from scientific identity only."""

    canonical = json.dumps(
        identity.model_dump(mode="json"),
        ensure_ascii=True,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return "dynamic-window-v1:" + hashlib.sha256(canonical).hexdigest()


class DynamicWindowPlan(BaseModel):
    """Backend-owned plan resolved from a cursor; clients cannot override it."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    session_identity: DynamicSessionIdentity
    cursor_sample: StrictInt = Field(ge=0)
    cursor_s: float | None = Field(default=None, ge=0)
    start_sample: StrictInt = Field(ge=0)
    end_sample: StrictInt = Field(gt=0)
    start_s: float = Field(ge=0)
    end_s: float = Field(gt=0)
    state: Literal["planned", "insufficient_data", "unavailable"] = "planned"
    reason: str | None = None

    @model_validator(mode="after")
    def validate_cursor(self) -> "DynamicWindowPlan":
        if self.end_sample <= self.start_sample:
            raise ValueError("planned dynamic window must have a positive sample range")
        if self.end_s <= self.start_s:
            raise ValueError("dynamic window seconds must have a positive range")
        if self.state != "planned" and not self.reason:
            raise ValueError("an unavailable window plan requires a reason")
        return self


class DynamicWindowRequest(BaseModel):
    """Client input for ensuring one backend-planned dynamic window."""

    model_config = ConfigDict(extra="forbid")

    recording_id: str = Field(min_length=1)
    algorithm_id: str = Field(min_length=1)
    definition_id: str | None = None
    definition_version: str | None = None
    config: dict[str, Any] = Field(default_factory=dict)
    cursor_sample: StrictInt = Field(ge=0)
    project_id: str | None = None
    request_id: str | None = Field(default=None, min_length=1)
    retry: bool = False
    retry_of_window_id: str | None = Field(default=None, min_length=1)

    @model_validator(mode="after")
    def validate_retry(self) -> "DynamicWindowRequest":
        if self.retry != (self.retry_of_window_id is not None):
            raise ValueError("retry requires the cancelled/failed window identity")
        if self.retry and not self.request_id:
            raise ValueError("retry requires a new client request identity")
        return self


class DynamicSubscriptionRequest(BaseModel):
    """Typed subscriber identity for one window consumer."""

    model_config = ConfigDict(extra="forbid")

    request_id: str | None = Field(default=None, min_length=1)
    project_id: str | None = None


class DynamicWindowResult(BaseModel):
    """Validated scientific payload boundary returned by a window executor."""

    model_config = ConfigDict(arbitrary_types_allowed=True, extra="forbid")

    identity: DynamicWindowIdentity
    state: OnDemandWindowState
    output_kind: str = Field(min_length=1)
    axes: dict[str, Any] = Field(default_factory=dict)
    axis_units: dict[str, str] = Field(default_factory=dict)
    arrays: dict[str, Any] = Field(default_factory=dict)
    array_units: dict[str, str] = Field(default_factory=dict)
    quality: str = Field(min_length=1)
    failure: dict[str, Any] | None = None
    evidence: dict[str, Any] = Field(default_factory=dict)
    provenance: dict[str, Any] = Field(default_factory=dict)
    sha256: str | None = None
    axis_shapes: dict[str, list[int]] = Field(default_factory=dict)
    array_shapes: dict[str, list[int]] = Field(default_factory=dict)

    @model_validator(mode="after")
    def validate_payload(self) -> "DynamicWindowResult":
        if set(self.axes) != set(self.axis_units):
            raise ValueError("window axes and axis units must match")
        if set(self.arrays) != set(self.array_units):
            raise ValueError("window arrays and array units must match")
        if self.state in {"rejected", "unavailable", "failed"} and self.failure is None:
            raise ValueError("unavailable window result requires structured failure")
        if self.state == "ready" and not self.provenance:
            raise ValueError("ready window result requires preprocessing provenance")
        if self.state == "ready":
            if not self.axes or not self.arrays:
                raise ValueError("ready window result requires nonempty axes and arrays")
            for name, value in {**self.axes, **self.arrays}.items():
                try:
                    numeric = np.asarray(value, dtype=float)
                    if numeric.size == 0:
                        raise ValueError(f"window payload {name!r} is empty")
                    if not np.isfinite(numeric).all():
                        raise ValueError(f"window payload {name!r} contains non-finite values")
                except (TypeError, ValueError) as exc:
                    if "contains non-finite" in str(exc):
                        raise
                    raise ValueError(f"window payload {name!r} is not numeric") from exc
        return self


class DynamicWindowFailure(BaseModel):
    """Stable, structured failure visible to API clients."""

    code: str = Field(min_length=1)
    message: str = Field(min_length=1)
    details: dict[str, Any] = Field(default_factory=dict)


class DynamicWindowResponse(BaseModel):
    """Serialized ensure/status response; arrays are present only when ready."""

    model_config = ConfigDict(arbitrary_types_allowed=True, extra="forbid")

    session_id: str = Field(min_length=1)
    window_id: str = Field(min_length=1)
    request_id: str = Field(min_length=1)
    identity: DynamicWindowIdentity
    state: OnDemandWindowState
    plan: DynamicWindowPlan
    result: DynamicWindowResult | None = None
    failure: DynamicWindowFailure | None = None
    range: dict[str, Any] | None = None
    quality: str | None = None
    subscription_id: str | None = None
    execution_mode: Literal["on_demand"] = "on_demand"
    scientific_cache_key: str | None = None

    @model_validator(mode="after")
    def validate_state_payload(self) -> "DynamicWindowResponse":
        if self.identity.start_sample != self.plan.start_sample or self.identity.end_sample != self.plan.end_sample:
            raise ValueError("window response identity does not match its planned sample range")
        session_fields = set(DynamicSessionIdentity.model_fields)
        if self.identity.model_dump(include=session_fields) != self.plan.session_identity.model_dump():
            raise ValueError("window response identity does not match its session identity")
        result_states = {"ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"}
        if self.state == "ready" and (self.result is None or self.result.state != "ready"):
            raise ValueError("ready window response requires a ready result")
        if self.state in {"rejected", "unavailable", "failed"} and self.failure is None and self.result is None:
            raise ValueError("terminal failed window response requires structured failure")
        if self.result is not None and self.result.state not in result_states:
            raise ValueError("window response result must be terminal")
        if self.result is not None and self.result.state != self.state:
            raise ValueError("window response state must match its result state")
        if self.result is not None and self.result.identity != self.identity:
            raise ValueError("window response identity must match its result identity")
        return self


@runtime_checkable
class DynamicWindowCapability(Protocol):
    """Optional algorithm capability consumed by a generic window scheduler."""

    manifest: AlgorithmManifest
    config_model: type[AlgorithmConfigBase]
    window_policy_version: str

    def can_execute_window(self, config: AlgorithmConfigBase) -> bool: ...

    def plan_window(
        self,
        inputs: AlgorithmInputs,
        config: AlgorithmConfigBase,
        cursor_sample: int,
        session_identity: DynamicSessionIdentity,
    ) -> DynamicWindowPlan: ...

    def execute_window(
        self,
        inputs: AlgorithmInputs,
        config: AlgorithmConfigBase,
        plan: DynamicWindowPlan,
        identity: DynamicWindowIdentity,
        check_cancelled: Callable[[], None] | None = None,
    ) -> DynamicWindowResult: ...

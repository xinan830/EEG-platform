"""Immutable persistence records for dynamic sessions, windows and subscriptions."""

from dataclasses import dataclass
from typing import Any

from app.algorithm_runtime.on_demand import OnDemandWindowState


@dataclass(frozen=True)
class DynamicSessionRecord:
    session_id: str
    project_id: str | None
    recording_id: str
    source_sha256: str
    algorithm_id: str
    definition_id: str
    definition_version: str
    definition_sha256: str
    implementation_build: str
    scientific_version: str
    implementation_identity: str
    configuration_sha256: str
    configuration: dict[str, Any]
    window_policy_version: str
    status: str
    created_at: str
    updated_at: str


@dataclass(frozen=True)
class DynamicWindowRecord:
    window_id: str
    session_id: str
    generation: int
    start_sample: int
    end_sample: int
    start_s: float
    end_s: float
    state: OnDemandWindowState
    quality: str
    failure: dict[str, Any] | None
    result_artifact_id: str | None
    created_at: str
    updated_at: str
    completed_at: str | None


@dataclass(frozen=True)
class DynamicSubscriptionRecord:
    subscription_id: str
    window_id: str
    request_id: str
    project_id: str | None
    state: str
    created_at: str
    updated_at: str
    cancelled_at: str | None

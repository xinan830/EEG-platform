"""Typed public and persistence models for analysis and validation runs."""

from __future__ import annotations

from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, Field


class RunStatus(str, Enum):
    QUEUED = "queued"
    RUNNING = "running"
    COMPLETED = "completed"
    GATE_FAILED = "gate_failed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    INTERRUPTED = "interrupted"


TERMINAL_RUN_STATUSES = {
    RunStatus.COMPLETED,
    RunStatus.GATE_FAILED,
    RunStatus.FAILED,
    RunStatus.CANCELLED,
}


class StructuredRunError(BaseModel):
    code: str
    message: str
    stage: str
    details: dict[str, Any] = Field(default_factory=dict)


class RunCreateRequest(BaseModel):
    recording_id: str = Field(min_length=1)
    analysis_type: Literal["legacy_analysis", "spectrum", "spectrogram"] = "legacy_analysis"
    config: dict[str, Any] = Field(default_factory=dict)
    definition_id: str | None = None
    definition_version: str | None = None
    preview: bool = False
    idempotency_key: str | None = Field(default=None, min_length=1, max_length=256)
    project_id: str | None = None
    batch_run_id: str | None = None


class AnalysisRun(BaseModel):
    run_id: str
    recording_id: str
    analysis_type: str
    status: RunStatus
    definition_id: str | None = None
    definition_version: str | None = None
    scientific_version: str
    implementation_version: str
    config: dict[str, Any]
    config_sha256: str
    cache_key: str
    requested_range: dict[str, float]
    actual_range: dict[str, float] | None = None
    channel_mapping: dict[str, Any]
    reference: dict[str, Any]
    filters: dict[str, Any]
    window: dict[str, Any]
    quality_rules: dict[str, Any]
    environment: dict[str, str]
    result_summary: dict[str, Any] | None = None
    error: StructuredRunError | None = None
    is_preview: bool = False
    reused_from_run_id: str | None = None
    project_id: str | None = None
    batch_run_id: str | None = None
    idempotency_key: str | None = None
    parent_run_id: str | None = None
    cancel_requested: bool = False
    created_at: str
    updated_at: str
    started_at: str | None = None
    completed_at: str | None = None


class RunArtifact(BaseModel):
    artifact_id: str
    run_id: str
    kind: str
    relative_path: str
    media_type: str
    byte_size: int
    sha256: str
    unit: str | None = None
    shape: dict[str, list[int]]
    created_at: str


class ValidationCreateRequest(BaseModel):
    kind: str = Field(min_length=1)
    subject_run_id: str | None = None
    algorithm_id: str | None = None
    algorithm_version: str | None = None
    dataset_identity: dict[str, Any] = Field(default_factory=dict)
    config_sha256: str = Field(min_length=1)
    tolerances: dict[str, float]
    expected: list[float]
    actual: list[float]


class ValidationRun(BaseModel):
    validation_id: str
    kind: str
    status: str
    subject_run_id: str | None = None
    algorithm_id: str | None = None
    algorithm_version: str | None = None
    dataset_identity: dict[str, Any]
    config_sha256: str
    tolerances: dict[str, float]
    expected_summary: dict[str, Any] | None = None
    actual_summary: dict[str, Any] | None = None
    max_absolute_error: float | None = None
    max_relative_error: float | None = None
    point_count: int = 0
    passed_point_count: int = 0
    pass_rate: float | None = None
    passed: bool | None = None
    environment: dict[str, str]
    error: StructuredRunError | None = None
    created_at: str
    completed_at: str | None = None

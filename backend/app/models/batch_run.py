"""Contracts for fixed local project batch execution."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


class BatchRunCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    project_id: str = Field(min_length=1)
    recording_ids: list[str] = Field(min_length=1, max_length=100)
    analysis_type: Literal["legacy_analysis", "spectrum", "spectrogram"] = "spectrum"
    config: dict[str, Any] = Field(default_factory=dict)
    definition_id: str | None = None
    definition_version: str | None = None
    idempotency_key: str | None = Field(default=None, min_length=1, max_length=256)


class BatchRun(BaseModel):
    batch_run_id: str
    project_id: str
    analysis_type: str
    definition_id: str | None = None
    definition_version: str | None = None
    config: dict[str, Any]
    config_sha256: str
    idempotency_key: str | None = None
    status: str
    created_at: str
    updated_at: str


class BatchRunItem(BaseModel):
    batch_run_id: str
    recording_id: str
    run_id: str | None = None
    outcome: str
    error_code: str | None = None

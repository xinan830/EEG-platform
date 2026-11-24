"""Typed contracts for immutable algorithm definitions and their versions."""

from __future__ import annotations

import re
from typing import Any, Literal

from pydantic import BaseModel, Field, field_validator


SEMVER_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$")
DefinitionStatus = Literal["draft", "testing", "validated_engineering", "research_use", "deprecated", "archived"]
VersionState = Literal["draft", "published"]


class DefinitionVersionDraft(BaseModel):
    semver: str
    graph: dict[str, Any]
    parameter_schema: dict[str, Any] = Field(default_factory=dict)
    inputs: dict[str, Any] = Field(default_factory=dict)
    outputs: dict[str, Any] = Field(default_factory=dict)
    units: dict[str, Any] = Field(default_factory=dict)
    quality_rules: dict[str, Any] = Field(default_factory=dict)
    references: list[str] = Field(default_factory=list)

    @field_validator("semver")
    @classmethod
    def validate_semver(cls, value: str) -> str:
        if not SEMVER_PATTERN.fullmatch(value):
            raise ValueError("semver must be MAJOR.MINOR.PATCH")
        return value


class DefinitionCreateRequest(BaseModel):
    name: str = Field(min_length=1, max_length=160)
    owner: str = Field(default="local-user", min_length=1, max_length=160)
    description: str = Field(default="", max_length=4000)


class AlgorithmDefinition(BaseModel):
    definition_id: str
    name: str
    owner: str
    status: DefinitionStatus
    description: str
    created_at: str
    updated_at: str


class AlgorithmDefinitionVersion(DefinitionVersionDraft):
    version_id: str
    definition_id: str
    state: VersionState
    digest_sha256: str
    created_at: str
    published_at: str | None = None

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class AlgorithmPresetCreateRequest(BaseModel):
    algorithm_id: str = Field(min_length=1)
    scientific_version: str = Field(min_length=1)
    name: str = Field(min_length=1, max_length=160)
    config: dict[str, Any] = Field(default_factory=dict)


class AlgorithmPresetUpdateRequest(BaseModel):
    name: str = Field(min_length=1, max_length=160)
    config: dict[str, Any] = Field(default_factory=dict)


class AlgorithmPreset(BaseModel):
    preset_id: str
    algorithm_id: str
    scientific_version: str
    name: str
    config: dict[str, Any]
    config_sha256: str
    created_at: str
    updated_at: str

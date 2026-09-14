"""Typed contracts for traceable scalar definition previews."""

from __future__ import annotations

import math

from pydantic import BaseModel, Field, field_validator, model_validator

from app.eeg_core.primitives.units import Unit
from app.models.algorithm_definition import DefinitionVersionDraft


class PreviewRange(BaseModel):
    start_s: float = Field(ge=0.0)
    end_s: float = Field(gt=0.0)

    @model_validator(mode="after")
    def validate_order(self) -> "PreviewRange":
        if self.end_s <= self.start_s:
            raise ValueError("preview end_s must be greater than start_s")
        return self


class PreviewScalarInput(BaseModel):
    value: float
    unit: Unit

    @field_validator("value")
    @classmethod
    def validate_finite(cls, value: float) -> float:
        if not math.isfinite(value):
            raise ValueError("preview scalar value must be finite")
        return value


class DefinitionPreviewRunRequest(BaseModel):
    recording_id: str = Field(min_length=1)
    time: PreviewRange
    draft: DefinitionVersionDraft
    inputs: dict[str, PreviewScalarInput] = Field(min_length=1)

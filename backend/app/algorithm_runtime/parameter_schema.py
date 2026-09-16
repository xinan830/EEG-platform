"""Frontend-safe descriptions of algorithm configuration fields."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field


ParameterValueType = Literal["string", "number", "integer", "boolean", "enum"]
ParameterVisibility = Literal["user", "advanced", "developer"]


class ParameterOption(BaseModel):
    value: str | int | float | bool
    label_zh: str


class AlgorithmParameter(BaseModel):
    key: str = Field(min_length=1)
    label_zh: str = Field(min_length=1)
    value_type: ParameterValueType
    required: bool = True
    default: Any = None
    unit: str | None = None
    affects_science: bool = True
    visibility: ParameterVisibility = "user"
    options: list[ParameterOption] = Field(default_factory=list)
    description_zh: str = ""


class ParameterSchema(BaseModel):
    parameters: list[AlgorithmParameter] = Field(default_factory=list)

    def model_post_init(self, __context: Any) -> None:
        keys = [item.key for item in self.parameters]
        if len(keys) != len(set(keys)):
            raise ValueError("parameter keys must be unique")
        if any(not item.affects_science for item in self.parameters):
            raise ValueError("display-only parameters do not belong in scientific configuration")

"""Frontend-safe descriptions of algorithm configuration fields."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator


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
    minimum: float | int | None = None
    maximum: float | int | None = None
    step: float | int | None = None
    affects_science: bool = True
    visibility: ParameterVisibility = "user"
    options: list[ParameterOption] = Field(default_factory=list)
    description_zh: str = ""

    @model_validator(mode="after")
    def validate_constraints(self) -> "AlgorithmParameter":
        numeric = self.value_type in {"number", "integer"}
        if not numeric and any(value is not None for value in (self.minimum, self.maximum, self.step)):
            raise ValueError(f"parameter {self.key!r} numeric constraints require a numeric value_type")
        if self.minimum is not None and self.maximum is not None and self.minimum > self.maximum:
            raise ValueError(f"parameter {self.key!r} minimum cannot exceed maximum")
        if self.step is not None and self.step <= 0:
            raise ValueError(f"parameter {self.key!r} step must be positive")
        if self.value_type == "integer" and any(
            value is not None and int(value) != value
            for value in (self.minimum, self.maximum, self.step)
        ):
            raise ValueError(f"parameter {self.key!r} integer constraints must be integral")
        option_values = [option.value for option in self.options]
        if self.value_type == "enum":
            if not option_values or len(option_values) != len(set(map(str, option_values))):
                raise ValueError(f"parameter {self.key!r} enum options must be unique and non-empty")
        elif self.options:
            raise ValueError(f"parameter {self.key!r} options require enum value_type")
        if self.default is not None:
            self._validate_value(self.default)
        return self

    def validate_value(self, value: Any) -> None:
        self._validate_value(value)

    def _validate_value(self, value: Any) -> None:
        if self.value_type == "number" and (isinstance(value, bool) or not isinstance(value, (int, float))):
            raise ValueError(f"parameter {self.key!r} requires a numeric value")
        if self.value_type == "integer" and (isinstance(value, bool) or not isinstance(value, int)):
            raise ValueError(f"parameter {self.key!r} requires an integer value")
        if self.value_type == "boolean" and not isinstance(value, bool):
            raise ValueError(f"parameter {self.key!r} requires a boolean value")
        if self.value_type == "string" and not isinstance(value, str):
            raise ValueError(f"parameter {self.key!r} requires a string value")
        if self.value_type == "enum" and value not in [option.value for option in self.options]:
            raise ValueError(f"parameter {self.key!r} has an unsupported option")
        if self.value_type in {"number", "integer"}:
            numeric = float(value)
            if self.minimum is not None and numeric < self.minimum:
                raise ValueError(f"parameter {self.key!r} is below its minimum")
            if self.maximum is not None and numeric > self.maximum:
                raise ValueError(f"parameter {self.key!r} exceeds its maximum")
            if self.step is not None:
                origin = float(self.minimum if self.minimum is not None else 0)
                remainder = (numeric - origin) / float(self.step)
                if abs(remainder - round(remainder)) > 1e-9:
                    raise ValueError(f"parameter {self.key!r} does not match its step")


class ParameterSchema(BaseModel):
    parameters: list[AlgorithmParameter] = Field(default_factory=list)

    def model_post_init(self, __context: Any) -> None:
        keys = [item.key for item in self.parameters]
        if len(keys) != len(set(keys)):
            raise ValueError("parameter keys must be unique")
        if any(not item.affects_science for item in self.parameters):
            raise ValueError("display-only parameters do not belong in scientific configuration")

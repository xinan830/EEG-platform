"""The only supported persisted official algorithm result shape."""

from typing import Annotated, Any, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

from app.algorithm_runtime.contracts import (
    DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
    OFFICIAL_ALGORITHM_RESULT_CONTRACT_VERSION,
)


class ResultFields(BaseModel):
    model_config = ConfigDict(extra="allow", allow_inf_nan=False)

    @model_validator(mode="before")
    @classmethod
    def reject_duplicate_fields(cls, value):
        if isinstance(value, dict) and ("warmup" in value or "value" in value):
            raise ValueError("use output.value and explicit canonical state")
        return value


class ScalarOutput(BaseModel):
    value: float | None
    unit: str = Field(min_length=1)
    model_config = ConfigDict(extra="allow", allow_inf_nan=False)


class ResultRange(BaseModel):
    start_s: float = Field(ge=0)
    end_s: float
    model_config = ConfigDict(allow_inf_nan=False)

    @model_validator(mode="after")
    def ordered(self):
        if self.end_s <= self.start_s:
            raise ValueError("result range must be positive")
        return self


class ScalarPoint(ResultFields):
    result_contract_version: Literal[DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION]
    time_s: float = Field(ge=0)
    window_start_s: float = Field(ge=0)
    window_end_s: float
    analysis_state: Literal["Partial", "Complete", "Rejected", "Unavailable"]
    output: ScalarOutput
    quality: dict[str, Any]

    @model_validator(mode="after")
    def ordered(self):
        if self.window_end_s <= self.window_start_s or self.time_s != self.window_end_s:
            raise ValueError("point must have a positive endpoint-aligned window")
        return self


class StaticMetric(ResultFields):
    output: ScalarOutput
    actual_range: ResultRange

    @model_validator(mode="before")
    @classmethod
    def no_series(cls, value):
        if isinstance(value, dict) and ("series" in value or "mode" in value):
            raise ValueError("static metric cannot carry dynamic fields")
        return value


class DynamicMetric(ResultFields):
    mode: Literal["dynamic"]
    result_contract_version: Literal[DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION]
    output: ScalarOutput
    actual_range: ResultRange
    series: list[ScalarPoint]


class StructuredOutput(BaseModel):
    kind: str = Field(min_length=1)
    mode: Literal["dynamic"] | None = None
    model_config = ConfigDict(extra="allow")


class StructuredWindow(ResultFields):
    start_sample: int = Field(ge=0, strict=True)
    end_sample: int = Field(gt=0, strict=True)
    state: Literal["Partial", "Complete", "Rejected", "Unavailable"]
    start_s: float = Field(ge=0)
    end_s: float
    quality: str

    @model_validator(mode="after")
    def ordered(self):
        if self.end_s <= self.start_s or self.end_sample <= self.start_sample:
            raise ValueError("structured window range must be positive")
        return self


class AxisMetadata(BaseModel):
    unit: str = Field(min_length=1)
    array_key: str = Field(min_length=1)
    length: int = Field(ge=0, strict=True)


class ArrayMetadata(BaseModel):
    unit: str = Field(min_length=1)
    shape: list[Annotated[int, Field(ge=0, strict=True)]]


class StructuredResult(ResultFields):
    output: StructuredOutput
    channel_order: list[str] = Field(min_length=1)
    requested_range: ResultRange
    actual_range: ResultRange
    axes: dict[str, AxisMetadata]
    arrays: dict[str, ArrayMetadata]
    windows: list[StructuredWindow] | None = None

    @model_validator(mode="after")
    def current_metadata(self):
        if self.output.mode == "dynamic" and self.windows is None:
            raise ValueError("dynamic structured output requires explicit windows")
        if self.output.mode is None and self.windows is not None:
            raise ValueError("static structured output cannot carry dynamic windows")
        if not self.axes or not self.arrays:
            raise ValueError("structured output requires axes and arrays")
        for axis in self.axes.values():
            if axis.array_key not in self.arrays or self.arrays[axis.array_key].shape != [axis.length]:
                raise ValueError("axis must reference its declared one-dimensional array")
        return self


class CurrentAlgorithmResultSummary(BaseModel):
    result_contract_version: Literal[OFFICIAL_ALGORITHM_RESULT_CONTRACT_VERSION]
    metric: DynamicMetric | StaticMetric | None = None
    structured: StructuredResult | None = None
    model_config = ConfigDict(extra="allow")

    @model_validator(mode="before")
    @classmethod
    def validate_metric_mode(cls, value):
        if isinstance(value, dict) and isinstance(value.get("metric"), dict):
            metric = value["metric"]
            # Do not accept a malformed dynamic payload as an extensible static metric.
            (DynamicMetric if metric.get("mode") == "dynamic" else StaticMetric).model_validate(metric)
        return value

    @model_validator(mode="after")
    def exactly_one_family(self):
        if (self.metric is None) == (self.structured is None):
            raise ValueError("official result requires exactly one result family")
        return self

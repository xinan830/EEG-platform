"""Validated static execution configuration for a saved user metric."""

from typing import Literal

from pydantic import BaseModel, Field, model_validator

from app.models.analysis_config import AnalysisTimeRange


class DefinitionMetricConfig(BaseModel):
    channel: str = Field(min_length=1, max_length=160)
    time: AnalysisTimeRange
    mode: Literal["static", "dynamic"] = "static"
    dynamic_window_s: Literal[10] = 10
    refresh_step_s: Literal[1] = 1

    @model_validator(mode="after")
    def validate_dynamic_range(self) -> "DefinitionMetricConfig":
        if self.mode == "dynamic" and self.time.end_s - self.time.start_s < self.dynamic_window_s:
            raise ValueError("动态算法分析区间至少需要 10 秒")
        return self

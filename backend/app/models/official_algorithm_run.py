"""Validated configuration for executable official composite algorithms."""

from typing import Literal

from pydantic import BaseModel, Field, model_validator

from app.models.analysis_config import AnalysisTimeRange


class OfficialAlgorithmRunConfig(BaseModel):
    algorithm_id: Literal["iapf", "theta_beta"]
    time: AnalysisTimeRange
    channel: str = Field(min_length=1, max_length=160)
    mode: Literal["static", "dynamic"] = "static"
    dynamic_window_s: float = Field(default=10, gt=0)
    refresh_step_s: float = Field(default=1, gt=0)

    @model_validator(mode="after")
    def validate_contract(self) -> "OfficialAlgorithmRunConfig":
        if self.mode == "dynamic" and self.time.end_s - self.time.start_s < 4.0:
            raise ValueError("dynamic official analysis requires at least 4 seconds")
        return self

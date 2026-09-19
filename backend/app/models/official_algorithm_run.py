"""Validated configuration for executable official composite algorithms."""

from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator

from app.models.analysis_config import AnalysisTimeRange


class OfficialAlgorithmRunConfig(BaseModel):
    algorithm_id: Literal["iapf", "theta_beta", "rbp", "faa"]
    scientific_version: str | None = Field(default=None, min_length=1, max_length=160)
    time: AnalysisTimeRange
    channel: str | None = Field(default=None, min_length=1, max_length=160)
    f4_channel: str | None = Field(default=None, min_length=1, max_length=160)
    mode: Literal["static", "dynamic"] = "static"
    dynamic_window_s: float = Field(default=10, gt=0)
    refresh_step_s: float = Field(default=1, gt=0)

    @model_validator(mode="after")
    def validate_contract(self) -> "OfficialAlgorithmRunConfig":
        if self.algorithm_id == "faa":
            if not self.channel or not self.f4_channel:
                raise ValueError("FAA requires explicit F3 and F4 source channels")
            if self.channel.casefold() == self.f4_channel.casefold():
                raise ValueError("FAA F3 and F4 source channels must be different")
        elif not self.channel:
            raise ValueError(f"{self.algorithm_id} requires an analysis channel")
        if self.mode == "dynamic" and self.time.end_s - self.time.start_s < 4.0:
            raise ValueError("dynamic official analysis requires at least 4 seconds")
        return self

    def runtime_config(self) -> dict[str, Any]:
        config: dict[str, Any] = {
            "channel": str(self.channel), "mode": self.mode,
            "start_s": float(self.time.start_s), "end_s": float(self.time.end_s),
            "window_s": float(self.dynamic_window_s), "step_s": float(self.refresh_step_s),
        }
        if self.algorithm_id == "faa":
            config["f4_channel"] = str(self.f4_channel)
        return config

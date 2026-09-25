"""Validated configuration for executable official composite algorithms."""

from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator

from app.models.analysis_config import AnalysisTimeRange


class OfficialAlgorithmRunConfig(BaseModel):
    algorithm_id: Literal["iapf", "theta_beta", "rbp", "faa", "peak_frequency", "band_ratio", "psd", "stft"]
    scientific_version: str | None = Field(default=None, min_length=1, max_length=160)
    time: AnalysisTimeRange
    channel: str | None = Field(default=None, min_length=1, max_length=160)
    f4_channel: str | None = Field(default=None, min_length=1, max_length=160)
    mode: Literal["static", "dynamic"] = "static"
    dynamic_window_s: float = Field(default=10, gt=0)
    refresh_step_s: float = Field(default=1, gt=0)
    low_hz: float | None = Field(default=None, ge=0)
    high_hz: float | None = Field(default=None, ge=0)
    numerator_low_hz: float | None = Field(default=None, ge=0)
    numerator_high_hz: float | None = Field(default=None, ge=0)
    denominator_low_hz: float | None = Field(default=None, ge=0)
    denominator_high_hz: float | None = Field(default=None, ge=0)

    @model_validator(mode="after")
    def validate_contract(self) -> "OfficialAlgorithmRunConfig":
        if self.algorithm_id == "faa":
            if not self.channel or not self.f4_channel:
                raise ValueError("FAA requires explicit F3 and F4 source channels")
            if self.channel.casefold() == self.f4_channel.casefold():
                raise ValueError("FAA F3 and F4 source channels must be different")
        elif not self.channel:
            raise ValueError(f"{self.algorithm_id} requires an analysis channel")
        if self.algorithm_id == "peak_frequency":
            if self.low_hz is None or self.high_hz is None:
                raise ValueError("peak_frequency requires low_hz and high_hz")
            if self.high_hz <= self.low_hz:
                raise ValueError("peak_frequency band must satisfy high_hz > low_hz")
        if self.algorithm_id == "band_ratio":
            bands = (
                self.numerator_low_hz, self.numerator_high_hz,
                self.denominator_low_hz, self.denominator_high_hz,
            )
            if any(value is None for value in bands):
                raise ValueError("band_ratio requires numerator and denominator bands")
            assert self.numerator_low_hz is not None and self.numerator_high_hz is not None
            assert self.denominator_low_hz is not None and self.denominator_high_hz is not None
            if self.numerator_high_hz <= self.numerator_low_hz:
                raise ValueError("numerator band must satisfy high_hz > low_hz")
            if self.denominator_high_hz <= self.denominator_low_hz:
                raise ValueError("denominator band must satisfy high_hz > low_hz")
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
        if self.algorithm_id == "peak_frequency":
            config.update({"low_hz": float(self.low_hz), "high_hz": float(self.high_hz)})
        if self.algorithm_id == "band_ratio":
            config.update({
                "numerator_low_hz": float(self.numerator_low_hz),
                "numerator_high_hz": float(self.numerator_high_hz),
                "denominator_low_hz": float(self.denominator_low_hz),
                "denominator_high_hz": float(self.denominator_high_hz),
            })
        return config

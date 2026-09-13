"""Validated request schema for configurable spectral analysis."""

from typing import Literal

from pydantic import BaseModel, Field, model_validator


class AnalysisTimeRange(BaseModel):
    start_s: float = Field(ge=0.0)
    end_s: float = Field(gt=0.0)

    @model_validator(mode="after")
    def validate_duration(self) -> "AnalysisTimeRange":
        if self.end_s <= self.start_s:
            raise ValueError("分析结束时间必须大于开始时间")
        if self.end_s - self.start_s < 4.0:
            raise ValueError("频谱分析区间至少需要 4 秒")
        if self.end_s - self.start_s > 120.0:
            raise ValueError("频谱分析区间不能超过 120 秒")
        return self


class AnalysisFrequencyRange(BaseModel):
    low_hz: float = Field(ge=1.0, le=30.0)
    high_hz: float = Field(ge=1.0, le=30.0)

    @model_validator(mode="after")
    def validate_order(self) -> "AnalysisFrequencyRange":
        if self.high_hz <= self.low_hz:
            raise ValueError("自定义频率上限必须大于下限")
        return self


class AnalysisConfigRequest(BaseModel):
    mode: Literal["static", "dynamic", "spectrogram"] = "static"
    channels: list[str] = Field(min_length=1)
    time: AnalysisTimeRange
    dynamic_window_s: Literal[5, 10, 20, 30] = 10
    refresh_step_s: Literal[1, 2, 5] = 1
    custom_frequency_range: AnalysisFrequencyRange | None = None

    @model_validator(mode="after")
    def validate_channels(self) -> "AnalysisConfigRequest":
        cleaned = [name.strip() for name in self.channels if name.strip()]
        if not cleaned or len(set(name.casefold() for name in cleaned)) != len(cleaned):
            raise ValueError("分析通道不能为空或重复")
        self.channels = cleaned
        if self.custom_frequency_range is not None and self.mode != "spectrogram":
            raise ValueError("自定义频段趋势仅适用于时频图分析")
        return self

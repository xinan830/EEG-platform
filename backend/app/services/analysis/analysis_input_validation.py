"""Validate analysis facts that require real recording metadata."""

from __future__ import annotations

import math
from typing import Sequence


class AnalysisInputValidationError(ValueError):
    def __init__(self, code: str, message: str, details: dict[str, object] | None = None):
        super().__init__(message)
        self.code = code
        self.details = details or {}


def validate_recording_analysis_input(
    duration_s: float,
    sfreq_hz: float,
    available_channels: Sequence[str],
    start_s: float,
    end_s: float,
    channels: Sequence[str],
    frequency_range: tuple[float, float] | None = None,
) -> None:
    if not math.isfinite(sfreq_hz) or sfreq_hz <= 0:
        raise AnalysisInputValidationError("ANALYSIS_SAMPLING_RATE_INVALID", "分析采样率必须大于 0", {"sfreq_hz": sfreq_hz})
    if not math.isfinite(duration_s) or duration_s < 0:
        raise AnalysisInputValidationError("ANALYSIS_DURATION_INVALID", "文件时长无效", {"duration_s": duration_s})
    if not all(math.isfinite(value) for value in (start_s, end_s)) or start_s < 0 or end_s <= start_s or end_s > duration_s + 1.5 / sfreq_hz:
        raise AnalysisInputValidationError("ANALYSIS_RANGE_OUT_OF_BOUNDS", "分析区间超出文件范围", {"start_s": start_s, "end_s": end_s, "duration_s": duration_s})
    existing = {channel.casefold() for channel in available_channels}
    missing = [channel for channel in channels if channel.casefold() not in existing]
    if missing:
        raise AnalysisInputValidationError("ANALYSIS_CHANNEL_UNAVAILABLE", "分析请求包含不存在的通道", {"channels": missing})
    if frequency_range is not None and frequency_range[1] >= sfreq_hz / 2:
        raise AnalysisInputValidationError("ANALYSIS_NYQUIST_INVALID", "分析频率上限必须低于 Nyquist 频率", {"frequency_range": frequency_range, "nyquist_hz": sfreq_hz / 2})

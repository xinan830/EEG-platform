"""Strict recording-relative sample boundaries; gaps never compress time."""

from __future__ import annotations

import math

INT64_MAX = (1 << 63) - 1
INT64_MIN = -(1 << 63)


def validate_sample_boundary(value: int) -> int:
    if type(value) is not int or not 0 <= value <= INT64_MAX:
        raise ValueError("sample boundary must be a nonnegative int64")
    return value


def cursor_sample_from_seconds(seconds: float, sampling_rate_hz: int, duration_samples: int) -> int:
    """Floor a finite in-range cursor without epsilon or forward rounding."""
    validate_sample_boundary(duration_samples)
    if type(sampling_rate_hz) is not int or sampling_rate_hz <= 0:
        raise ValueError("sampling rate must be a positive integer")
    if isinstance(seconds, bool) or not isinstance(seconds, (int, float)):
        raise ValueError("cursor seconds must be numeric")
    if not math.isfinite(seconds) or not 0 <= seconds <= duration_samples / sampling_rate_hz:
        raise ValueError("cursor seconds are outside the recording")
    product = seconds * sampling_rate_hz
    if not math.isfinite(product) or product >= 1 << 63:
        raise ValueError("cursor seconds exceed int64 precision; use sample coordinates")
    sample = math.floor(product)
    if sample > duration_samples:
        raise ValueError("cursor sample is outside the recording")
    return sample


def relative_sample_from_counter(counter: int, first_counter: int) -> int:
    """Subtract validated monotonic device counters, retaining missing positions."""
    for value in (counter, first_counter):
        if type(value) is not int or not INT64_MIN <= value <= INT64_MAX:
            raise ValueError("device counter must be int64")
    return validate_sample_boundary(counter - first_counter)

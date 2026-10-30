"""Shared result contract for bounded historical and live EEG sample reads."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Generic, Literal, TypeVar

import numpy as np


SourceIdentity = TypeVar("SourceIdentity")


@dataclass(frozen=True)
class UnavailableSampleInterval:
    start_sample: int
    end_sample: int
    reason: Literal["missing_value", "overwritten", "not_yet_available", "source_gap"]
    channels: tuple[str, ...]


@dataclass(frozen=True)
class SampleBlock(Generic[SourceIdentity]):
    source_identity: SourceIdentity
    start_sample: int
    end_sample: int
    sampling_rate_hz: float
    channel_names: tuple[str, ...]
    channel_units: tuple[str, ...]
    values_v: np.ndarray
    unavailable_intervals: tuple[UnavailableSampleInterval, ...]

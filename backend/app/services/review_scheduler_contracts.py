"""Stable identity fields shared by review scheduling and scientific workers."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal


ReviewTaskLane = Literal[
    "waveform_preview",
    "window_independent_analysis",
    "stateful_analysis",
    "batch_analysis",
]


@dataclass(frozen=True)
class ReviewTargetIdentity:
    trace_id: str
    generation: int
    recording_id: str
    algorithm_id: str
    configuration_fingerprint: str
    cursor_sample: int
    sample_rate_hz: float
    channel_order: tuple[str, ...]
    lane: ReviewTaskLane
    playback_delivery_id: str | None = None

    def __post_init__(self) -> None:
        if not self.trace_id.strip():
            raise ValueError("trace_id is required")
        if self.generation < 0 or self.cursor_sample < 0:
            raise ValueError("generation and cursor_sample must be non-negative")
        if not self.recording_id.strip() or not self.algorithm_id.strip():
            raise ValueError("recording_id and algorithm_id are required")
        if not self.configuration_fingerprint.strip():
            raise ValueError("configuration_fingerprint is required")
        if self.sample_rate_hz <= 0:
            raise ValueError("sample_rate_hz must be positive")
        if not self.channel_order or any(not name.strip() for name in self.channel_order):
            raise ValueError("channel_order is required")

    @property
    def scientific_context_key(self) -> tuple[object, ...]:
        return (
            self.recording_id,
            self.algorithm_id,
            self.configuration_fingerprint,
            self.sample_rate_hz,
            self.channel_order,
        )


__all__ = ["ReviewTaskLane", "ReviewTargetIdentity"]

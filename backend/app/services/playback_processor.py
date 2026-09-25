"""Processor boundary used by the offline playback service.

The current implementation is still the legacy streaming processor. Keeping
construction behind this boundary lets a future Runtime-backed processor be
introduced without changing playback session state, controls, or websocket
payloads.
"""

from __future__ import annotations

from typing import Any, Protocol


class PlaybackProcessor(Protocol):
    """Minimum streaming contract required by ``PlaybackSession``."""

    iapf_global: float | None
    iapf_lock_candidates: list[Any]
    iapf_live: float | None
    iapf_locked: bool

    def reset_buffers(self) -> None: ...
    def start_segment(self) -> None: ...
    def start_rbp_session(self) -> None: ...
    def set_filters(self, notch: float, bp_low: float, bp_high: float) -> None: ...
    def push_chunk(self, chunk: Any) -> None: ...
    def add_segment_chunk(self, chunk: Any) -> None: ...
    def try_lock_iapf(self) -> Any: ...
    def should_update(self) -> bool: ...
    def calculate_metrics(self) -> tuple[Any, Any, Any, Any, Any]: ...
    def elapsed_s(self) -> float: ...
    def fatigue_index(self) -> Any: ...
    def hai_index(self) -> Any: ...
    def estimate_aperiodic_exponents(self) -> Any: ...


def create_playback_processor(sfreq: float, channel_names: list[str]) -> PlaybackProcessor:
    """Create the current playback processor implementation.

    This import is intentionally local: the legacy processor is a playback
    compatibility dependency, not a backend-wide scientific authority.
    """
    from app.legacy.playback_processor import EEGProcessor

    return EEGProcessor(sfreq, channel_names)

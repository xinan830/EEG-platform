"""Deterministic analysis-window construction shared by dynamic algorithms."""

from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Literal

from .errors import InvalidAnalysisWindowError


WindowState = Literal["Partial", "Complete"]


@dataclass(frozen=True)
class AnalysisWindow:
    start_s: float
    end_s: float
    center_s: float
    state: WindowState = "Complete"

    @property
    def warmup(self) -> bool:
        """Legacy view retained for callers that have not migrated yet."""
        return self.state == "Partial"


@dataclass(frozen=True)
class DynamicAnalysisFrame:
    """One endpoint-aligned unit of dynamic EEG analysis.

    This is deliberately limited to time planning.  Signal quality and spectral
    evidence are attached by the backend signal-access boundary after the
    corresponding *actual* window has been loaded.
    """

    time_s: float
    window_start_s: float
    window_end_s: float
    requested_window_s: float
    state: WindowState

    @property
    def warmup(self) -> bool:
        """Legacy view retained for algorithm adapters and old serializers."""
        return self.state == "Partial"

    @property
    def actual_window_s(self) -> float:
        return self.window_end_s - self.window_start_s


def build_windows(
    start_s: float,
    end_s: float,
    *,
    duration_s: float,
    window_s: float,
    step_s: float,
) -> list[AnalysisWindow]:
    values = (start_s, end_s, duration_s, window_s, step_s)
    if not all(math.isfinite(value) for value in values):
        raise InvalidAnalysisWindowError("analysis window values must be finite")
    if start_s < 0 or end_s <= start_s:
        raise InvalidAnalysisWindowError("analysis range must satisfy 0 <= start < end")
    if duration_s <= 0 or window_s <= 0 or step_s <= 0:
        raise InvalidAnalysisWindowError("duration, window, and step must be positive")

    bounded_start = min(start_s, duration_s)
    bounded_end = min(end_s, duration_s)
    if bounded_end <= bounded_start or bounded_end - bounded_start < window_s:
        return []

    last_start = bounded_end - window_s
    windows: list[AnalysisWindow] = []
    cursor = bounded_start
    epsilon = max(1e-9, step_s * 1e-9)
    while cursor <= last_start + epsilon:
        actual_end = cursor + window_s
        windows.append(AnalysisWindow(cursor, actual_end, cursor + window_s / 2.0, "Complete"))
        cursor += step_s
    return windows


def build_playback_windows(
    start_s: float,
    end_s: float,
    *,
    duration_s: float,
    window_s: float,
    step_s: float,
    minimum_window_s: float = 4.0,
    allow_warmup: bool = True,
) -> list[AnalysisWindow]:
    """Build endpoint-aligned windows for playback-synchronised analysis.

    A point is anchored to the time at which its EEG window ends, never to the
    window centre.  A shorter first window is allowed once one complete Welch
    segment is available and is explicitly marked as a warm-up result.
    """
    values = (start_s, end_s, duration_s, window_s, step_s, minimum_window_s)
    if not all(math.isfinite(value) for value in values):
        raise InvalidAnalysisWindowError("analysis window values must be finite")
    if start_s < 0 or end_s <= start_s:
        raise InvalidAnalysisWindowError("analysis range must satisfy 0 <= start < end")
    if duration_s <= 0 or window_s <= 0 or step_s <= 0 or minimum_window_s <= 0:
        raise InvalidAnalysisWindowError("duration and analysis window values must be positive")

    bounded_start = min(start_s, duration_s)
    bounded_end = min(end_s, duration_s)
    if bounded_end <= bounded_start:
        return []
    epsilon = max(1e-9, step_s * 1e-9)
    windows: list[AnalysisWindow] = []
    # A catch-up request beginning at recording time zero must preserve every
    # available warm-up endpoint.  Returning only the final short range would
    # make a renderer join (for example) 4 s directly to 10 s and falsely
    # suggest that intermediate algorithm values were identical or absent.
    # Only recording-time zero can produce partial warm-up windows.  A later
    # catch-up request deliberately starts at a prior trailing-window boundary
    # (for example 2 s when resuming endpoints 12–16 s).  Treating that
    # boundary as a new warm-up origin would plan an invalid 2–4 s window.
    if bounded_start <= epsilon and allow_warmup:
        cursor = minimum_window_s
        warmup_end = min(window_s, bounded_end)
        while cursor < warmup_end - epsilon:
            windows.append(AnalysisWindow(bounded_start, cursor, cursor, "Partial"))
            cursor += step_s
        if bounded_end < window_s - epsilon:
            if not windows or abs(windows[-1].end_s - bounded_end) > epsilon:
                windows.append(AnalysisWindow(bounded_start, bounded_end, bounded_end, "Partial"))
            return windows

    first_end = window_s if bounded_start <= epsilon else bounded_start + window_s
    cursor = first_end
    while cursor <= bounded_end + epsilon:
        actual_end = min(cursor, bounded_end)
        windows.append(AnalysisWindow(max(bounded_start, actual_end - window_s), actual_end, actual_end, "Complete"))
        cursor += step_s
    return windows


def build_dynamic_analysis_frames(
    start_s: float,
    end_s: float,
    *,
    duration_s: float,
    window_s: float,
    step_s: float,
    minimum_window_s: float = 4.0,
    allow_warmup: bool = True,
) -> list[DynamicAnalysisFrame]:
    """Plan the sole time contract used by dynamic algorithm adapters."""
    return [
        DynamicAnalysisFrame(
            time_s=window.end_s,
            window_start_s=window.start_s,
            window_end_s=window.end_s,
            requested_window_s=window_s,
            state=window.state,
        )
        for window in build_playback_windows(
            start_s,
            end_s,
            duration_s=duration_s,
            window_s=window_s,
            step_s=step_s,
            minimum_window_s=minimum_window_s,
            allow_warmup=allow_warmup,
        )
    ]

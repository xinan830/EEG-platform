"""Deterministic analysis-window construction shared by dynamic algorithms."""

from __future__ import annotations

from dataclasses import dataclass
import math

from .errors import InvalidAnalysisWindowError


@dataclass(frozen=True)
class AnalysisWindow:
    start_s: float
    end_s: float
    center_s: float
    warmup: bool = False


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
        windows.append(AnalysisWindow(cursor, actual_end, cursor + window_s / 2.0))
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
    first_end = bounded_start + window_s
    if first_end > bounded_end + epsilon:
        if bounded_end - bounded_start < minimum_window_s - epsilon:
            return []
        return [AnalysisWindow(bounded_start, bounded_end, bounded_end, True)]

    windows: list[AnalysisWindow] = []
    cursor = first_end
    while cursor <= bounded_end + epsilon:
        actual_end = min(cursor, bounded_end)
        windows.append(AnalysisWindow(max(bounded_start, actual_end - window_s), actual_end, actual_end))
        cursor += step_s
    return windows

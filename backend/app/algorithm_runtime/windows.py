"""Deterministic analysis-window construction shared by dynamic algorithms."""

from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Literal
from collections.abc import Iterator, Sequence

from .errors import InvalidAnalysisWindowError
from .execution_control import ExecutionControl


WindowState = Literal["Partial", "Complete"]


def controlled_frames(frames: Sequence[DynamicAnalysisFrame], execution_context: ExecutionControl | None = None) -> Iterator[DynamicAnalysisFrame]:
    """Count actual completed windows, including rejected scientific results."""
    for frame in frames:
        if execution_context is not None:
            execution_context.window_started()
        yield frame
        if execution_context is not None:
            execution_context.window_completed()


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


def latest_dynamic_analysis_frame_at_or_before(
    start_s: float,
    end_s: float,
    *,
    duration_s: float,
    window_s: float,
    step_s: float,
    cursor_s: float,
    minimum_window_s: float = 4.0,
    allow_warmup: bool = True,
) -> DynamicAnalysisFrame | None:
    """Return the latest planned frame whose endpoint has been reached.

    This is the bounded counterpart to ``build_dynamic_analysis_frames`` for
    interactive playback.  It deliberately never selects a future endpoint.
    The arithmetic mirrors ``build_playback_windows`` so a jump does not need
    to materialize every preceding frame in a long recording.
    """
    values = (start_s, end_s, duration_s, window_s, step_s, cursor_s, minimum_window_s)
    if not all(math.isfinite(value) for value in values):
        raise InvalidAnalysisWindowError("analysis window values must be finite")
    if start_s < 0 or end_s <= start_s or cursor_s < 0:
        raise InvalidAnalysisWindowError("analysis range and cursor must be non-negative and ordered")
    if duration_s <= 0 or window_s <= 0 or step_s <= 0 or minimum_window_s <= 0:
        raise InvalidAnalysisWindowError("duration and analysis window values must be positive")

    bounded_start = min(start_s, duration_s)
    bounded_end = min(end_s, duration_s)
    reached_end = min(cursor_s, bounded_end)
    if reached_end <= bounded_start:
        return None
    epsilon = max(1e-9, step_s * 1e-9)
    candidates: list[AnalysisWindow] = []

    if bounded_start <= epsilon and allow_warmup:
        warmup_end = min(window_s, bounded_end)
        if reached_end >= minimum_window_s - epsilon and minimum_window_s < warmup_end - epsilon:
            index = math.floor((min(reached_end, warmup_end - epsilon) - minimum_window_s) / step_s + epsilon)
            if index >= 0:
                endpoint = minimum_window_s + index * step_s
                if endpoint < warmup_end - epsilon and endpoint <= reached_end + epsilon:
                    candidates.append(AnalysisWindow(0.0, endpoint, endpoint, "Partial"))
        if bounded_end < window_s - epsilon and reached_end >= bounded_end - epsilon:
            candidates.append(AnalysisWindow(0.0, bounded_end, bounded_end, "Partial"))

    first_end = window_s if bounded_start <= epsilon else bounded_start + window_s
    if reached_end >= first_end - epsilon and first_end <= bounded_end + epsilon:
        index = math.floor((min(reached_end, bounded_end) - first_end) / step_s + epsilon)
        endpoint = min(first_end + max(index, 0) * step_s, bounded_end)
        if endpoint <= reached_end + epsilon:
            candidates.append(AnalysisWindow(max(bounded_start, endpoint - window_s), endpoint, endpoint, "Complete"))

    if not candidates:
        return None
    window = max(candidates, key=lambda item: item.end_s)
    return DynamicAnalysisFrame(
        time_s=window.end_s,
        window_start_s=window.start_s,
        window_end_s=window.end_s,
        requested_window_s=window_s,
        state=window.state,
    )

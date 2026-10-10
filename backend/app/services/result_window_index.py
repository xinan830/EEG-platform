"""Resolve a cursor against immutable saved metadata, without reading EEG."""

from __future__ import annotations

from bisect import bisect_right
from collections.abc import Iterable

from app.core.recording_time import validate_sample_boundary
from app.models.result_window import SampleRange, SavedResultWindow, WindowSelection


class SavedWindowIndex:
    """Validate once, then select the unique latest non-future endpoint."""

    def __init__(
        self,
        duration_samples: int,
        windows: Iterable[SavedResultWindow],
        gaps: Iterable[SampleRange] = (),
    ) -> None:
        self.duration_samples = validate_sample_boundary(duration_samples)
        self.windows = tuple(windows)
        self.gaps = tuple(gaps)
        self._validate_windows()
        self._validate_gaps()
        self._ends = tuple(window.end_sample for window in self.windows)
        self._gap_starts = tuple(gap.start_sample for gap in self.gaps)

    def _validate_windows(self) -> None:
        ids: set[str] = set()
        previous_end = previous_index = -1
        for window in self.windows:
            if window.end_sample > self.duration_samples:
                raise ValueError("saved window exceeds recording duration")
            if window.end_sample <= previous_end or window.window_index <= previous_index:
                raise ValueError("saved endpoints and window indices must be strictly increasing")
            if window.window_id in ids:
                raise ValueError("saved window IDs must be unique")
            ids.add(window.window_id)
            previous_end, previous_index = window.end_sample, window.window_index

    def _validate_gaps(self) -> None:
        previous_end = -1
        for gap in self.gaps:
            if gap.end_sample > self.duration_samples or gap.start_sample < previous_end:
                raise ValueError("gaps must be ordered, disjoint and inside the recording")
            previous_end = gap.end_sample

    def select(self, cursor_sample: int) -> WindowSelection:
        """Do not replace a rejected result or a gap with older valid values."""
        validate_sample_boundary(cursor_sample)
        if cursor_sample > self.duration_samples:
            raise ValueError("cursor sample is outside the recording")
        index = bisect_right(self._ends, cursor_sample) - 1
        window = self.windows[index] if index >= 0 else None
        gap_index = bisect_right(self._gap_starts, cursor_sample) - 1
        if gap_index >= 0 and cursor_sample < self.gaps[gap_index].end_sample:
            return WindowSelection(
                cursor_sample=cursor_sample, status="gap", window=None,
                reason_codes=("DATA_GAP",),
            )
        if window is None:
            return WindowSelection(
                cursor_sample=cursor_sample, status="insufficient_data",
                reason_codes=("NO_COMPLETED_WINDOW",),
            )
        return WindowSelection(
            cursor_sample=cursor_sample,
            status="available" if window.quality_status == "valid" else window.quality_status,
            window=window,
            reason_codes=window.reason_codes,
            has_uncovered_tail=index == len(self.windows) - 1 and cursor_sample > window.end_sample,
        )

"""Stateful local-only display filtering for desktop acquisition batches."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.services.waveform_filter import DISPLAY_FILTER_CONTRACT, DisplaySignalFilter


@dataclass
class LiveFilterSession:
    sampling_rate_hz: int
    channel_count: int
    eeg_channel_indexes: tuple[int, ...]
    low_cut_hz: float
    high_cut_hz: float
    notch_hz: float | None
    display_filter: DisplaySignalFilter

    def process_array(self, values: np.ndarray, sample_count: int) -> np.ndarray:
        values = np.asarray(values, dtype=np.float64)
        if values.size != sample_count * self.channel_count:
            raise ValueError("batch values length does not match sample_count and channel_count")
        source = values.reshape(sample_count, self.channel_count)
        filtered = source.copy()
        filtered[:, self.eeg_channel_indexes] = self.display_filter.process(source[:, self.eeg_channel_indexes])
        return filtered.reshape(-1)

    def process(self, values_v: list[float], sample_count: int) -> list[float]:
        return self.process_array(np.asarray(values_v, dtype=np.float64), sample_count).tolist()


class LiveFilterService:
    """Owns transient filter state; it never writes recordings, Runs, or SQLite."""

    def __init__(self) -> None:
        self._sessions: dict[str, LiveFilterSession] = {}

    def create(
        self,
        session_id: str,
        sampling_rate_hz: int,
        channel_count: int,
        eeg_channel_indexes: list[int],
        low_cut_hz: float,
        high_cut_hz: float,
        notch_hz: float | None,
    ) -> LiveFilterSession:
        if not session_id:
            raise ValueError("session_id is required")
        if sampling_rate_hz <= 0 or channel_count <= 0:
            raise ValueError("sampling_rate_hz and channel_count must be positive")
        indexes = tuple(sorted(set(eeg_channel_indexes)))
        if not indexes or indexes[0] < 0 or indexes[-1] >= channel_count:
            raise ValueError("at least one valid EEG channel index is required")
        if notch_hz is not None and float(notch_hz) not in (50.0, 60.0):
            raise ValueError("notch_hz must be null, 50, or 60")
        display_filter = DisplaySignalFilter(
            sfreq=sampling_rate_hz,
            channel_count=len(indexes),
            notch_freq=notch_hz,
            bp_low=low_cut_hz,
            bp_high=high_cut_hz,
        )
        session = LiveFilterSession(
            sampling_rate_hz,
            channel_count,
            indexes,
            float(low_cut_hz),
            float(high_cut_hz),
            None if notch_hz is None else float(notch_hz),
            display_filter,
        )
        self._sessions[session_id] = session
        return session

    def process(self, session_id: str, values_v: list[float], sample_count: int) -> list[float]:
        try:
            return self._sessions[session_id].process(values_v, sample_count)
        except KeyError as exc:
            raise KeyError("live filter session does not exist") from exc

    def process_array(self, session_id: str, values: np.ndarray, sample_count: int) -> np.ndarray:
        try:
            return self._sessions[session_id].process_array(values, sample_count)
        except KeyError as exc:
            raise KeyError("live filter session does not exist") from exc

    def close(self, session_id: str) -> None:
        self._sessions.pop(session_id, None)

    @property
    def contract(self) -> dict[str, object]:
        return dict(DISPLAY_FILTER_CONTRACT)

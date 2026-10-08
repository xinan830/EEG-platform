"""Sample-addressed V/float64 readers; no whole-file production preload."""

from __future__ import annotations

from bisect import bisect_right
from pathlib import Path
from typing import Any

import numpy as np

from app.services.wpf_recording import _index_batches, inspect_wpf_recording


class ArraySampleReader:
    """Adapter for in-memory sources and synthetic test providers."""
    def __init__(self, data: Any, sfreq: float, names: list[str]):
        self.data = np.asarray(data, dtype=float)
        self.sfreq = float(sfreq)
        self.names = tuple(names)
        self.sample_count = len(self.data)

    def read(self, start: int, stop: int, channels: list[int] | None = None) -> np.ndarray:
        return self.data[start:stop] if channels is None else self.data[start:stop, channels]

    def close(self) -> None:
        pass


class WpfSampleReader:
    def __init__(self, directory: str | Path):
        self.facts = inspect_wpf_recording(directory)
        self.batches = _index_batches(self.facts.root, len(self.facts.channels))
        self.first_counter = self.batches[0][0]
        self.starts = [item[0] - self.first_counter for item in self.batches]
        self.sfreq = self.facts.sampling_rate_hz
        self.sample_count = self.facts.sample_count
        self.names = tuple(self.facts.channels[i].label for i in self.facts.eeg_indexes)

    def read(self, start: int, stop: int, channels: list[int] | None = None) -> np.ndarray:
        if not 0 <= start <= stop <= self.sample_count:
            raise ValueError("sample range is outside the recording")
        selected = list(range(len(self.names))) if channels is None else channels
        stream_indexes = [self.facts.eeg_indexes[i] for i in selected]
        values = np.full((stop - start, len(selected)), np.nan, dtype=float)
        first_batch = max(0, bisect_right(self.starts, start) - 1)
        for counter, count, channel_count, _ticks, offset, path in self.batches[first_batch:]:
            batch_start = counter - self.first_counter
            if batch_start >= stop:
                break
            left, right = max(start, batch_start), min(stop, batch_start + count)
            if right <= left:
                continue
            with path.open("rb") as stream:
                stream.seek(offset + (left - batch_start) * channel_count * 8)
                data = np.fromfile(stream, dtype="<f8", count=(right - left) * channel_count)
            if data.size != (right - left) * channel_count:
                raise ValueError(f"WPF sample block is truncated: {path.name}")
            values[left - start:right - start] = data.reshape(-1, channel_count)[:, stream_indexes]
        return values

    def close(self) -> None:
        pass


class EdfSampleReader:
    def __init__(self, raw: Any):
        self.raw = raw
        self.sfreq = float(raw.info["sfreq"])
        self.names = tuple(raw.ch_names)
        self.sample_count = int(raw.n_times)

    def read(self, start: int, stop: int, channels: list[int] | None = None) -> np.ndarray:
        return self.raw.get_data(picks=channels, start=start, stop=stop).T

    def close(self) -> None:
        self.raw.close()

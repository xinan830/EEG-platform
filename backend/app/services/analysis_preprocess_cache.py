"""Bounded in-process cache for continuous offline preprocessing results."""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from threading import RLock
from typing import Any

import numpy as np


@dataclass(frozen=True)
class PreprocessedRecording:
    data: np.ndarray
    sfreq: float
    channel_names: tuple[str, ...]
    owner: Any = None


class AnalysisPreprocessCache:
    """Small LRU cache; entries are immutable by convention after insertion."""

    def __init__(self, max_entries: int = 2, max_resident_bytes: int = 128 * 1024 * 1024):
        if max_entries < 1 or max_resident_bytes < 1:
            raise ValueError("分析预处理缓存容量必须大于零")
        self.max_entries = int(max_entries)
        self.max_resident_bytes = int(max_resident_bytes)
        self._entries: OrderedDict[tuple[str, str], PreprocessedRecording] = OrderedDict()
        self._lock = RLock()

    def get(self, recording_id: str, algorithm_version: str) -> PreprocessedRecording | None:
        key = (recording_id, algorithm_version)
        with self._lock:
            value = self._entries.get(key)
            if value is not None:
                self._entries.move_to_end(key)
            return value

    def put(self, recording_id: str, algorithm_version: str, value: PreprocessedRecording) -> None:
        key = (recording_id, algorithm_version)
        with self._lock:
            self._entries[key] = value
            self._entries.move_to_end(key)
            while len(self._entries) > self.max_entries or sum(
                item.data.nbytes for item in self._entries.values() if not isinstance(item.data, np.memmap)
            ) > self.max_resident_bytes:
                self._entries.popitem(last=False)

    def clear_recording(self, recording_id: str) -> None:
        with self._lock:
            for key in [key for key in self._entries if key[0] == recording_id]:
                self._entries.pop(key, None)

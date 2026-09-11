"""线程安全、容量受限的显示滤波状态检查点缓存。"""

from __future__ import annotations

from collections import OrderedDict
from threading import Lock
from typing import TypeAlias

from app.services.waveform_filter import FilterState


CheckpointKey: TypeAlias = tuple[object, ...]


class FilterCheckpointCache:
    """按参数组合做 LRU 淘汰，并限制每组可保留的检查点数量。"""

    def __init__(self, max_groups: int = 32, max_checkpoints_per_group: int = 256):
        if max_groups <= 0 or max_checkpoints_per_group <= 0:
            raise ValueError("检查点缓存容量必须大于 0")
        self.max_groups = max_groups
        self.max_checkpoints_per_group = max_checkpoints_per_group
        self._groups: OrderedDict[CheckpointKey, OrderedDict[int, FilterState]] = OrderedDict()
        self._lock = Lock()

    def nearest(self, key: CheckpointKey, target_sample: int) -> tuple[int, FilterState | None]:
        with self._lock:
            checkpoints = self._groups.get(key)
            if not checkpoints:
                return 0, None
            self._groups.move_to_end(key)
            eligible = [sample for sample in checkpoints if sample <= target_sample]
            if not eligible:
                return 0, None
            sample = max(eligible)
            return sample, checkpoints[sample]

    def put(self, key: CheckpointKey, sample: int, state: FilterState) -> None:
        with self._lock:
            checkpoints = self._groups.setdefault(key, OrderedDict())
            checkpoints[sample] = state
            checkpoints.move_to_end(sample)
            while len(checkpoints) > self.max_checkpoints_per_group:
                checkpoints.popitem(last=False)
            self._groups.move_to_end(key)
            while len(self._groups) > self.max_groups:
                self._groups.popitem(last=False)

    def clear_recording(self, recording_id: str) -> None:
        with self._lock:
            matching = [key for key in self._groups if key and key[0] == recording_id]
            for key in matching:
                self._groups.pop(key, None)

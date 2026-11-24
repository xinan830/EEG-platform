"""Owned temporary maps for unchanged continuous offline preprocessing."""

from __future__ import annotations

import tempfile
import shutil
import weakref
from pathlib import Path
from typing import Any

import numpy as np

from app.scientific.primitives import preprocess_blocks
from app.algorithm_runtime.execution_control import ExecutionControl
from app.services.analysis_preprocess_cache import PreprocessedRecording
from app.algorithm_runtime.errors import DynamicWindowResourceExceededError


class MappedArrays:
    def __init__(self, resource_policy=None):
        self.directory = tempfile.TemporaryDirectory(prefix="brain-analysis-")
        self.maps: dict[int, np.memmap] = {}
        self.sequence = 0
        self.resource_policy = resource_policy
        # Registered after TemporaryDirectory's finalizer so mmap handles close
        # before Windows attempts directory cleanup during interpreter exit.
        self._finalizer = weakref.finalize(self, _cleanup_maps, self.maps, self.directory)

    def allocate(self, shape) -> np.memmap:
        requested = int(np.prod(shape)) * np.dtype("float64").itemsize
        if self.resource_policy is not None:
            used = sum(array.nbytes for array in self.maps.values())
            free = shutil.disk_usage(self.directory.name).free
            if (used + requested > self.resource_policy.max_preparation_bytes or
                    free - requested < self.resource_policy.minimum_free_disk_bytes):
                raise DynamicWindowResourceExceededError("连续预处理临时文件超出资源预算。",
                    detail={"used_bytes": used, "requested_bytes": requested, "free_bytes": free,
                            "limit_bytes": self.resource_policy.max_preparation_bytes,
                            "minimum_free_disk_bytes": self.resource_policy.minimum_free_disk_bytes})
        self.sequence += 1
        array = np.memmap(Path(self.directory.name) / f"array-{self.sequence}.bin",
                          mode="w+", dtype="float64", shape=shape)
        self.maps[id(array)] = array
        return array

    def release(self, array: np.ndarray) -> None:
        mapped = self.maps.pop(id(array), None)
        if mapped is not None:
            path = Path(mapped.filename)
            mapped._mmap.close()
            path.unlink(missing_ok=True)

    def close(self) -> None:
        self._finalizer()


def _cleanup_maps(maps, directory):
    for mapped in list(maps.values()):
        mapped._mmap.close()
    maps.clear()
    directory.cleanup()


def mapped_preprocess(recordings: Any, recording: Any, *, low_hz: float,
                      high_hz: float, notch_hz: float | None, notch_q: float = 30.0,
                      channels: list[str] | None = None, execution_context: ExecutionControl | None = None) -> PreprocessedRecording:
    policy = getattr(execution_context, "preparation_resource_policy", None)
    owner = MappedArrays() if policy is None else MappedArrays(policy)
    session = None
    def check_cancelled():
        if execution_context is not None:
            execution_context.check_cancelled()
    try:
        session = recordings.open_access_session(recording)
        lookup = {name.casefold(): name for name in session.channel_names}
        names = tuple(session.channel_names) if channels is None else tuple(lookup[name.casefold()] for name in channels)
        shape = (session.sample_count, len(names))
        source = owner.allocate(shape)
        target = owner.allocate(shape)
        block_samples = max(1, min(65536, (8 * 1024**2) // (8 * len(names))))
        for start in range(0, session.sample_count, block_samples):
            check_cancelled()
            stop = min(start + block_samples, session.sample_count)
            source[start:stop] = session.read_samples(
                start, stop, names, check_cancelled=check_cancelled,
            ).values_v
        preprocess_blocks(source, target, session.sampling_rate_hz, owner.allocate,
                          low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
                          notch_q=notch_q, check_cancelled=check_cancelled, release=owner.release,
                          block_samples=block_samples)
        owner.release(source)
        target.flush()
        return PreprocessedRecording(target, session.sampling_rate_hz, names, owner)
    except BaseException:
        owner.close()
        raise
    finally:
        if session is not None:
            session.close()

"""Owned temporary memory-mapped arrays for offline preprocessing."""

from __future__ import annotations

import shutil
import tempfile
import weakref
from pathlib import Path

import numpy as np

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

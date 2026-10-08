"""Owned temporary maps for unchanged continuous offline preprocessing."""

from __future__ import annotations

import tempfile
import weakref
from pathlib import Path
from typing import Any

import numpy as np

from app.scientific.primitives import preprocess_blocks
from app.algorithm_runtime.execution_control import ExecutionControl
from app.services.analysis_preprocess_cache import PreprocessedRecording


class MappedArrays:
    def __init__(self):
        self.directory = tempfile.TemporaryDirectory(prefix="brain-analysis-")
        self.maps: dict[int, np.memmap] = {}
        self.sequence = 0
        # Registered after TemporaryDirectory's finalizer so mmap handles close
        # before Windows attempts directory cleanup during interpreter exit.
        self._finalizer = weakref.finalize(self, _cleanup_maps, self.maps, self.directory)

    def allocate(self, shape) -> np.memmap:
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
    reader = recordings.open_sample_reader(recording)
    owner = MappedArrays()
    def check_cancelled():
        if execution_context is not None:
            execution_context.check_cancelled()
    try:
        lookup = {name.casefold(): index for index, name in enumerate(reader.names)}
        indexes = list(range(len(reader.names))) if channels is None else [lookup[name.casefold()] for name in channels]
        names = tuple(reader.names[index] for index in indexes)
        shape = (reader.sample_count, len(indexes))
        source = owner.allocate(shape)
        target = owner.allocate(shape)
        block_samples = 65536
        for start in range(0, reader.sample_count, block_samples):
            check_cancelled()
            stop = min(start + block_samples, reader.sample_count)
            source[start:stop] = reader.read(start, stop, indexes)
        preprocess_blocks(source, target, reader.sfreq, owner.allocate,
                          low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
                          notch_q=notch_q, check_cancelled=check_cancelled, release=owner.release)
        owner.release(source)
        target.flush()
        return PreprocessedRecording(target, reader.sfreq, names, owner)
    except BaseException:
        owner.close()
        raise
    finally:
        reader.close()

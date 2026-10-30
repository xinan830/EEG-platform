"""Sample-addressed V/float64 readers; no whole-file production preload."""

from __future__ import annotations

from bisect import bisect_right
from collections import OrderedDict
from concurrent.futures import Future
import mmap
from pathlib import Path
from threading import RLock
from typing import Any, Callable, Protocol

import numpy as np

from app.services.wpf_recording import (
    WpfRecordingError,
    WpfRecordingFacts,
    inspect_wpf_recording,
    wpf_source_signature,
)


class SampleReader(Protocol):
    def read(
        self,
        start: int,
        stop: int,
        channels: list[int] | None = None,
        check_cancelled: Callable[[], None] | None = None,
    ) -> np.ndarray: ...

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]: ...

    def close(self) -> None: ...


class WpfRecordingFactsCache:
    """Reuse validated immutable WPF metadata without sharing reader state."""

    def __init__(self, max_entries: int = 4):
        if max_entries < 1:
            raise ValueError("WPF 记录 facts 缓存容量必须大于零")
        self._max_entries = max_entries
        self._lock = RLock()
        self._entries: OrderedDict[tuple[str, str], WpfRecordingFacts] = OrderedDict()
        self._inflight: dict[tuple[str, str], Future[WpfRecordingFacts]] = {}

    def remember(self, facts: WpfRecordingFacts) -> None:
        key = (str(facts.root), facts.source_sha256)
        with self._lock:
            self._store(key, facts)

    def get(self, directory: str | Path, expected_sha256: str | None) -> WpfRecordingFacts:
        root = Path(directory).expanduser().resolve()
        if not expected_sha256:
            return inspect_wpf_recording(root)
        key = (str(root), expected_sha256)
        signature = wpf_source_signature(root)
        with self._lock:
            facts = self._entries.get(key)
            if facts is not None and facts.source_signature == signature:
                self._entries.move_to_end(key)
                return facts
            self._entries.pop(key, None)
            future = self._inflight.get(key)
            owner = future is None
            if owner:
                future = Future()
                self._inflight[key] = future
        if not owner:
            return future.result()
        try:
            facts = inspect_wpf_recording(root)
            if facts.source_sha256 != expected_sha256:
                raise WpfRecordingError(
                    "WPF_RECORDING_SOURCE_IDENTITY_MISMATCH",
                    "WPF 记录内容与已注册的源身份不一致。",
                )
            if facts.source_signature != wpf_source_signature(root):
                raise WpfRecordingError(
                    "WPF_RECORDING_SOURCE_CHANGED_DURING_READ",
                    "读取期间 WPF 记录源发生变化，请重新注册后再分析。",
                )
            with self._lock:
                self._store(key, facts)
            future.set_result(facts)
            return facts
        except BaseException as exc:
            future.set_exception(exc)
            future.exception()
            raise
        finally:
            with self._lock:
                self._inflight.pop(key, None)

    def _store(self, key: tuple[str, str], facts: WpfRecordingFacts) -> None:
        self._entries[key] = facts
        self._entries.move_to_end(key)
        while len(self._entries) > self._max_entries:
            self._entries.popitem(last=False)


class ArraySampleReader:
    """Adapter for in-memory sources and synthetic test providers."""
    def __init__(self, data: Any, sfreq: float, names: list[str]):
        self.data = np.asarray(data, dtype=float)
        self.sfreq = float(sfreq)
        self.names = tuple(names)
        self.sample_count = len(self.data)

    def read(self, start: int, stop: int, channels: list[int] | None = None,
             check_cancelled: Callable[[], None] | None = None) -> np.ndarray:
        if check_cancelled is not None:
            check_cancelled()
        return self.data[start:stop] if channels is None else self.data[start:stop, channels]

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]:
        return ()

    def close(self) -> None:
        pass


class WpfSampleReader:
    def __init__(self, source: str | Path | WpfRecordingFacts):
        self.facts = source if isinstance(source, WpfRecordingFacts) else inspect_wpf_recording(source)
        self.batches = self.facts.batches
        self.first_counter = self.batches[0][0]
        self.starts = [item[0] - self.first_counter for item in self.batches]
        self.sfreq = self.facts.sampling_rate_hz
        self.sample_count = self.facts.sample_count
        self.names = tuple(self.facts.channels[i].label for i in self.facts.eeg_indexes)
        self._maps: dict[Path, mmap.mmap] = {}
        self._map_lock = RLock()

    def _mapped_file(self, path: Path) -> mmap.mmap:
        with self._map_lock:
            mapped = self._maps.get(path)
            if mapped is not None:
                return mapped
            try:
                with path.open("rb") as stream:
                    mapped = mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ)
            except (OSError, ValueError) as exc:
                raise ValueError(f"WPF sample block is unreadable: {path.name}") from exc
            self._maps[path] = mapped
            return mapped

    def read(self, start: int, stop: int, channels: list[int] | None = None,
             check_cancelled: Callable[[], None] | None = None) -> np.ndarray:
        if not 0 <= start <= stop <= self.sample_count:
            raise ValueError("sample range is outside the recording")
        selected = list(range(len(self.names))) if channels is None else channels
        stream_indexes = [self.facts.eeg_indexes[i] for i in selected]
        values = np.full((stop - start, len(selected)), np.nan, dtype=float)
        first_batch = max(0, bisect_right(self.starts, start) - 1)
        group: list[tuple[int, int, int, int, int, Path]] = []
        group_path: Path | None = None

        def flush_group() -> None:
            if not group:
                return
            if check_cancelled is not None:
                check_cancelled()
            path = group[0][5]
            mapped = self._mapped_file(path)
            first_offset = group[0][4]
            _last_counter, last_count, last_channels, _ticks, last_offset, _ = group[-1]
            end_offset = last_offset + last_count * last_channels * 8
            if end_offset > len(mapped):
                raise ValueError(f"WPF sample block is truncated: {path.name}")
            # WPF recordings contain tiny payloads; one contiguous copy avoids one I/O fault per batch.
            raw = mapped[first_offset:end_offset]
            for counter, count, channel_count, _ticks, offset, _ in group:
                if check_cancelled is not None:
                    check_cancelled()
                batch_start = counter - self.first_counter
                left, right = max(start, batch_start), min(stop, batch_start + count)
                if right <= left:
                    continue
                relative_offset = offset - first_offset
                data = np.frombuffer(
                    raw, dtype="<f8", count=count * channel_count,
                    offset=relative_offset,
                ).reshape(count, channel_count)
                values[left - start:right - start] = data[
                    left - batch_start:right - batch_start, stream_indexes
                ]

        for batch in self.batches[first_batch:]:
            counter, count, channel_count, _ticks, _offset, path = batch
            if check_cancelled is not None:
                check_cancelled()
            batch_start = counter - self.first_counter
            if batch_start >= stop:
                break
            if group_path is not None and path != group_path:
                flush_group()
                group.clear()
            group_path = path
            group.append(batch)
        flush_group()
        return values

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]:
        """Return missing source intervals in recording-relative sample coordinates."""
        gaps: list[range] = []
        cursor = start
        first_batch = max(0, bisect_right(self.starts, start) - 1)
        for counter, count, _channels, _ticks, _offset, _path in self.batches[first_batch:]:
            batch_start = counter - self.first_counter
            if batch_start >= stop:
                break
            batch_end = batch_start + count
            if batch_start > cursor:
                gaps.append(range(cursor, min(batch_start, stop)))
            cursor = max(cursor, batch_end)
            if cursor >= stop:
                break
        if cursor < stop:
            gaps.append(range(cursor, stop))
        return tuple(gaps)

    def close(self) -> None:
        with self._map_lock:
            maps = tuple(self._maps.values())
            self._maps.clear()
            for mapped in maps:
                mapped.close()


class EdfSampleReader:
    def __init__(self, raw: Any):
        self.raw = raw
        self.sfreq = float(raw.info["sfreq"])
        self.names = tuple(raw.ch_names)
        self.sample_count = int(raw.n_times)

    def read(self, start: int, stop: int, channels: list[int] | None = None,
             check_cancelled: Callable[[], None] | None = None) -> np.ndarray:
        if check_cancelled is not None:
            check_cancelled()
        values = self.raw.get_data(picks=channels, start=start, stop=stop).T
        if check_cancelled is not None:
            check_cancelled()
        return values

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]:
        return ()

    def close(self) -> None:
        self.raw.close()

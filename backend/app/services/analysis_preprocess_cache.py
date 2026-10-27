"""Bounded in-process cache for continuous offline preprocessing results."""

from __future__ import annotations

from collections import OrderedDict
from concurrent.futures import Future
from dataclasses import dataclass
from threading import RLock
from typing import Any, Callable

import numpy as np

from app.services.prepared_source_contract import PreparedSourceContract


@dataclass(frozen=True)
class PreprocessedRecording:
    data: np.ndarray
    sfreq: float
    channel_names: tuple[str, ...]
    owner: Any = None
    contract: PreparedSourceContract | None = None


@dataclass(frozen=True)
class PreparedSourceCacheLookup:
    value: PreprocessedRecording | None
    miss_reason: str | None = None

    @property
    def hit(self) -> bool:
        return self.value is not None


class AnalysisPreprocessCache:
    """Small LRU cache; entries are immutable by convention after insertion."""

    def __init__(self, max_entries: int = 2, max_resident_bytes: int = 128 * 1024 * 1024):
        if max_entries < 1 or max_resident_bytes < 1:
            raise ValueError("分析预处理缓存容量必须大于零")
        self.max_entries = int(max_entries)
        self.max_resident_bytes = int(max_resident_bytes)
        self._entries: OrderedDict[tuple[str, str], PreprocessedRecording] = OrderedDict()
        self._inflight: dict[tuple[str, str], Future[PreprocessedRecording]] = {}
        self._lock = RLock()

    def get(self, recording_id: str, algorithm_version: str,
            expected_contract: PreparedSourceContract | None = None) -> PreprocessedRecording | None:
        return self.lookup(recording_id, algorithm_version, expected_contract).value

    def lookup(self, recording_id: str, algorithm_version: str,
               expected_contract: PreparedSourceContract | None = None) -> PreparedSourceCacheLookup:
        key = (recording_id, algorithm_version)
        with self._lock:
            value = self._entries.get(key)
            if value is None:
                return PreparedSourceCacheLookup(None, "not_cached")
            if expected_contract is not None and value.contract is None:
                # A hash collision or stale in-process entry must be treated as
                # a miss, never as permission to reuse an unverifiable source.
                return PreparedSourceCacheLookup(None, "contract_unavailable")
            if expected_contract is not None and not value.contract.matches(expected_contract):
                return PreparedSourceCacheLookup(None, "contract_mismatch")
            self._entries.move_to_end(key)
            return PreparedSourceCacheLookup(value)

    def put(self, recording_id: str, algorithm_version: str, value: PreprocessedRecording) -> None:
        key = (recording_id, algorithm_version)
        with self._lock:
            self._entries[key] = value
            self._entries.move_to_end(key)
            while len(self._entries) > self.max_entries or sum(
                item.data.nbytes for item in self._entries.values() if not isinstance(item.data, np.memmap)
            ) > self.max_resident_bytes:
                self._entries.popitem(last=False)

    def get_or_create(
        self,
        recording_id: str,
        algorithm_version: str,
        factory: Callable[[], PreprocessedRecording],
        expected_contract: PreparedSourceContract | None = None,
    ) -> PreprocessedRecording:
        """Return a cached source while coalescing concurrent cold starts.

        The factory runs outside the cache lock. Only the first caller performs
        the expensive read/filter/map operation; concurrent callers wait for
        that same result instead of starting duplicate preprocessing work.
        """
        key = (recording_id, algorithm_version)
        lookup = self.lookup(recording_id, algorithm_version, expected_contract)
        if lookup.hit:
            return lookup.value  # type: ignore[return-value]

        with self._lock:
            lookup = self.lookup(recording_id, algorithm_version, expected_contract)
            if lookup.hit:
                return lookup.value  # type: ignore[return-value]
            pending = self._inflight.get(key)
            if pending is None:
                pending = Future()
                self._inflight[key] = pending
                owner = True
            else:
                owner = False

        if not owner:
            value = pending.result()
            if expected_contract is not None and (
                value.contract is None or not value.contract.matches(expected_contract)
            ):
                # A contract collision must retain the same miss semantics as a
                # normal lookup; do not let an in-flight result bypass identity
                # validation for a waiting caller.
                return self.get_or_create(
                    recording_id, algorithm_version, factory,
                    expected_contract=expected_contract,
                )
            return value

        try:
            value = factory()
            self.put(recording_id, algorithm_version, value)
            pending.set_result(value)
            return value
        except BaseException as error:
            pending.set_exception(error)
            # The owner re-raises below; mark the Future's exception as
            # retrieved so a failed cold start with no waiters is not logged as
            # an unhandled Future exception during cleanup.
            pending.exception()
            raise
        finally:
            with self._lock:
                if self._inflight.get(key) is pending:
                    self._inflight.pop(key, None)

    def clear_recording(self, recording_id: str) -> None:
        with self._lock:
            for key in [key for key in self._entries if key[0] == recording_id]:
                self._entries.pop(key, None)

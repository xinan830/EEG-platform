"""Dedicated scientific preprocessing layer for live acquisition window algorithms.

Isolates live scientific filtering (fixed 1-50 Hz window-local zero-phase contract)
from live display filtering (user-adjustable causal 1-30 Hz), while coalescing and
caching identical rolling windows across concurrent live adapters (RBP / PSD / STFT / Topomap).
"""

from __future__ import annotations

from collections import OrderedDict
from concurrent.futures import Future
from dataclasses import dataclass
from threading import RLock

import numpy as np

from app.scientific.contracts.preprocessing import ScientificFilterSpec
from app.scientific.primitives import DEFAULT_SPECTRAL_GATEWAY, ScientificSpectralGateway
from app.services.recordings.sample_access_contract import SampleBlock


@dataclass(frozen=True)
class LivePreparedWindow:
    values_v: np.ndarray
    sampling_rate_hz: float
    channel_names: tuple[str, ...]
    start_sample: int
    end_sample: int
    evidence: dict[str, object]


class LiveScientificPreprocessingService:
    """Bounded LRU + single-flight scientific preprocessing cache for live windows."""

    def __init__(
        self,
        gateway: ScientificSpectralGateway = DEFAULT_SPECTRAL_GATEWAY,
        *,
        max_entries: int = 16,
    ) -> None:
        if max_entries < 1:
            raise ValueError("实时科学预处理缓存容量必须大于零")
        self._gateway = gateway
        self._max_entries = int(max_entries)
        self._entries: OrderedDict[tuple, LivePreparedWindow] = OrderedDict()
        self._inflight: dict[tuple, Future[LivePreparedWindow]] = {}
        self._global_epoch = 0
        self._session_epochs: dict[str, int] = {}
        self._lock = RLock()
        self._hits = 0
        self._misses = 0
        self._coalesced = 0

    def prepare_window(
        self,
        session_id: str,
        block: SampleBlock,
        spec: ScientificFilterSpec,
    ) -> LivePreparedWindow:
        """Validate, filter, and cache a live SampleBlock under a fixed scientific contract."""
        spec.validate(block.sampling_rate_hz)
        if block.unavailable_intervals or not np.isfinite(block.values_v).all():
            raise ValueError("当前实时窗口包含缺失或非有限样本，无法执行科学预处理。")

        key = self._cache_key(session_id, block, spec)
        if key is None:
            return self._compute(block, spec)

        leader = False
        future: Future[LivePreparedWindow] | None = None
        start_global_epoch = 0
        start_session_epoch = 0
        with self._lock:
            cached = self._entries.get(key)
            if cached is not None:
                self._entries.move_to_end(key)
                self._hits += 1
                return cached
            existing = self._inflight.get(key)
            if existing is not None:
                self._coalesced += 1
                future = existing
            else:
                future = Future()
                self._inflight[key] = future
                self._misses += 1
                start_global_epoch = self._global_epoch
                start_session_epoch = self._session_epochs.get(session_id, 0)
                leader = True

        if not leader:
            return future.result()

        try:
            window = self._compute(block, spec)
        except Exception as exc:
            with self._lock:
                if self._inflight.get(key) is future:
                    self._inflight.pop(key, None)
            future.set_exception(exc)
            raise

        with self._lock:
            if self._inflight.get(key) is future:
                self._inflight.pop(key, None)
            if (
                self._global_epoch == start_global_epoch
                and self._session_epochs.get(session_id, 0) == start_session_epoch
            ):
                self._entries[key] = window
                self._entries.move_to_end(key)
                while len(self._entries) > self._max_entries:
                    self._entries.popitem(last=False)
        future.set_result(window)
        return window

    def invalidate_session(self, session_id: str) -> int:
        with self._lock:
            self._session_epochs[session_id] = self._session_epochs.get(session_id, 0) + 1
            removed = [key for key in self._entries if key[0] == session_id]
            for key in removed:
                self._entries.pop(key, None)
            for key in [k for k in self._inflight if k[0] == session_id]:
                self._inflight.pop(key, None)
            return len(removed)

    def clear(self) -> None:
        with self._lock:
            self._global_epoch += 1
            self._entries.clear()
            self._inflight.clear()

    def stats(self) -> dict[str, int]:
        with self._lock:
            return {
                "hits": self._hits,
                "misses": self._misses,
                "coalesced": self._coalesced,
                "entries": len(self._entries),
            }

    def _compute(self, block: SampleBlock, spec: ScientificFilterSpec) -> LivePreparedWindow:
        prepared_values = np.asarray(
            self._gateway.preprocess(
                block.values_v,
                block.sampling_rate_hz,
                low_hz=spec.low_hz,
                high_hz=spec.high_hz,
                notch_hz=spec.notch_hz,
                notch_q=spec.notch_q,
            ),
            dtype=np.float64,
        )
        prepared_values.flags.writeable = False
        montage_revision = getattr(block.source_identity, "montage_revision", None)
        return LivePreparedWindow(
            values_v=prepared_values,
            sampling_rate_hz=float(block.sampling_rate_hz),
            channel_names=tuple(block.channel_names),
            start_sample=int(block.start_sample),
            end_sample=int(block.end_sample),
            evidence={
                **spec.to_evidence(),
                "sampling_rate_hz": float(block.sampling_rate_hz),
                "montage_revision": montage_revision,
                "unit": "V",
                "channel_order": list(block.channel_names),
            },
        )

    @staticmethod
    def _cache_key(session_id: str, block: SampleBlock, spec: ScientificFilterSpec) -> tuple | None:
        identity = block.source_identity
        stream_rev = getattr(identity, "stream_revision", None)
        schema_rev = getattr(identity, "schema_revision", None)
        montage_rev = getattr(identity, "montage_revision", None)
        if not session_id or not stream_rev or not schema_rev or montage_rev is None:
            return None
        return (
            str(session_id),
            str(stream_rev),
            str(schema_rev),
            int(montage_rev),
            int(block.start_sample),
            int(block.end_sample),
            round(float(block.sampling_rate_hz), 6),
            tuple(block.channel_names),
            spec.fingerprint(),
        )

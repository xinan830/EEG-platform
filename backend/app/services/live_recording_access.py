"""Bounded, identity-bound sample access for live acquisition batches."""

from __future__ import annotations

import threading
from collections import deque
from dataclasses import dataclass
from typing import Callable, Sequence

import numpy as np

from app.services.sample_access_contract import SampleBlock, UnavailableSampleInterval


class LiveRecordingAccessError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class LiveSourceIdentity:
    acquisition_session_id: str
    stream_revision: str
    schema_revision: str


@dataclass(frozen=True)
class LiveRangeSnapshot:
    retained_start_sample: int | None
    high_watermark_sample: int | None
    capacity_samples: int
    overwritten_before_sample: int | None


@dataclass
class _Chunk:
    start_sample: int
    values: np.ndarray

    @property
    def end_sample(self) -> int:
        return self.start_sample + self.values.shape[0]


class LiveRecordingAccessSession:
    """Owns one bounded live source; it never exposes the desktop ring buffer."""

    def __init__(
        self,
        identity: LiveSourceIdentity,
        sampling_rate_hz: float,
        channel_names: Sequence[str],
        channel_units: Sequence[str],
        capacity_samples: int,
    ) -> None:
        if not identity.acquisition_session_id or not identity.stream_revision or not identity.schema_revision:
            raise LiveRecordingAccessError("LIVE_SESSION_ID_INVALID", "实时采集会话身份不能为空。")
        if sampling_rate_hz <= 0 or capacity_samples <= 0:
            raise LiveRecordingAccessError("LIVE_METADATA_INVALID", "实时采样率和保留容量必须为正数。")
        names = tuple(channel_names)
        units = tuple(channel_units)
        if not names or len(names) != len(units) or len({name.casefold() for name in names}) != len(names):
            raise LiveRecordingAccessError("LIVE_CHANNELS_INVALID", "实时通道名称与单位必须一一对应且唯一。")
        if any(not name or not unit for name, unit in zip(names, units)):
            raise LiveRecordingAccessError("LIVE_CHANNELS_INVALID", "实时通道名称和单位不能为空。")
        if any(unit != "V" for unit in units):
            raise LiveRecordingAccessError("LIVE_CHANNEL_UNITS_INVALID", "实时样本访问只接受 V 单位的 EEG 通道。")
        self.identity = identity
        self.sampling_rate_hz = float(sampling_rate_hz)
        self.channel_names = names
        self.channel_units = units
        self.capacity_samples = int(capacity_samples)
        self._chunks: deque[_Chunk] = deque()
        self._gaps: deque[tuple[int, int]] = deque()
        self._retained_samples = 0
        self._high_watermark: int | None = None
        self._closed = False
        self._lock = threading.RLock()

    def append(
        self,
        first_sample_counter: int,
        values_v: np.ndarray,
        sample_count: int,
    ) -> LiveRangeSnapshot:
        if isinstance(first_sample_counter, bool) or first_sample_counter < 0:
            raise LiveRecordingAccessError("LIVE_SAMPLE_COUNTER_INVALID", "实时样本计数不能为负数。")
        values = np.asarray(values_v, dtype=np.float64)
        if sample_count <= 0 or values.size != sample_count * len(self.channel_names):
            raise LiveRecordingAccessError("LIVE_BATCH_SHAPE_INVALID", "实时批次形状与样本计数不一致。")
        values = values.reshape(sample_count, len(self.channel_names))
        if np.isinf(values).any():
            raise LiveRecordingAccessError("LIVE_BATCH_NONFINITE", "实时批次不能包含无穷值。")
        with self._lock:
            self._ensure_open()
            expected = None if self._high_watermark is None else self._high_watermark + 1
            if expected is not None and first_sample_counter < expected:
                raise LiveRecordingAccessError("LIVE_BATCH_OVERLAP", "实时批次与已接收样本范围重叠。")
            if expected is not None and first_sample_counter > expected:
                self._gaps.append((expected, first_sample_counter))
            self._chunks.append(_Chunk(first_sample_counter, values.copy()))
            self._retained_samples += sample_count
            self._high_watermark = first_sample_counter + sample_count - 1
            self._trim()
            return self.range_snapshot()

    def read_samples(
        self,
        start_sample: int,
        end_sample: int,
        channels: Sequence[str] | None = None,
        *,
        check_cancelled: Callable[[], None] | None = None,
    ) -> SampleBlock[LiveSourceIdentity]:
        if not isinstance(start_sample, int) or not isinstance(end_sample, int) or start_sample < 0 or end_sample <= start_sample:
            raise LiveRecordingAccessError("LIVE_SAMPLE_RANGE_INVALID", "实时读取范围必须是正数半开样本区间。")
        requested = self.channel_names if channels is None else tuple(channels)
        lookup = {name.casefold(): index for index, name in enumerate(self.channel_names)}
        try:
            indexes = [lookup[name.casefold()] for name in requested]
        except KeyError as exc:
            raise LiveRecordingAccessError("LIVE_CHANNEL_NOT_FOUND", f"实时通道不存在：{exc.args[0]}") from exc
        if not requested or len(set(name.casefold() for name in requested)) != len(requested):
            raise LiveRecordingAccessError("LIVE_CHANNELS_INVALID", "实时读取通道不能重复或为空。")
        if check_cancelled is not None:
            check_cancelled()
        values = np.full((end_sample - start_sample, len(requested)), np.nan, dtype=np.float64)
        unavailable: list[UnavailableSampleInterval] = []
        with self._lock:
            self._ensure_open()
            retained_start = self._chunks[0].start_sample if self._chunks else None
            if retained_start is None:
                unavailable.append(UnavailableSampleInterval(start_sample, end_sample, "not_yet_available", requested))
            else:
                if start_sample < retained_start:
                    unavailable.append(UnavailableSampleInterval(start_sample, min(end_sample, retained_start), "overwritten", requested))
                if self._high_watermark is None or end_sample > self._high_watermark + 1:
                    unavailable.append(UnavailableSampleInterval(max(start_sample, (self._high_watermark or -1) + 1), end_sample, "not_yet_available", requested))
                for chunk in self._chunks:
                    left, right = max(start_sample, chunk.start_sample), min(end_sample, chunk.end_sample)
                    if left < right:
                        values[left - start_sample:right - start_sample, :] = chunk.values[left - chunk.start_sample:right - chunk.start_sample, :][:, indexes]
                for gap_start, gap_end in self._gaps:
                    left, right = max(start_sample, gap_start), min(end_sample, gap_end)
                    if left < right:
                        unavailable.append(UnavailableSampleInterval(left, right, "source_gap", requested))
            for index, name in enumerate(requested):
                missing = np.flatnonzero(~np.isfinite(values[:, index]))
                if missing.size:
                    breaks = np.flatnonzero(np.diff(missing) > 1)
                    starts = np.r_[missing[0], missing[breaks + 1]]
                    ends = np.r_[missing[breaks], missing[-1]] + 1
                    unavailable.extend(
                        UnavailableSampleInterval(start_sample + int(left), start_sample + int(right), "missing_value", (name,))
                        for left, right in zip(starts, ends)
                    )
            block = SampleBlock(
                self.identity,
                start_sample,
                end_sample,
                self.sampling_rate_hz,
                requested,
                tuple(self.channel_units[index] for index in indexes),
                values,
                tuple(unavailable),
            )
            if check_cancelled is not None:
                check_cancelled()
            return block

    def range_snapshot(self) -> LiveRangeSnapshot:
        with self._lock:
            retained_start = self._chunks[0].start_sample if self._chunks else None
            return LiveRangeSnapshot(
                retained_start,
                self._high_watermark,
                self.capacity_samples,
                retained_start,
            )

    def close(self) -> None:
        with self._lock:
            self._closed = True
            self._chunks.clear()
            self._gaps.clear()
            self._retained_samples = 0

    def _trim(self) -> None:
        while self._retained_samples > self.capacity_samples and self._chunks:
            chunk = self._chunks[0]
            remove = min(self._retained_samples - self.capacity_samples, chunk.values.shape[0])
            if remove == chunk.values.shape[0]:
                self._chunks.popleft()
            else:
                chunk.start_sample += remove
                chunk.values = chunk.values[remove:]
            self._retained_samples -= remove
        retained_start = self._chunks[0].start_sample if self._chunks else None
        while self._gaps and retained_start is not None and self._gaps[0][1] <= retained_start:
            self._gaps.popleft()
        if self._gaps and retained_start is not None and self._gaps[0][0] < retained_start:
            _, end = self._gaps[0]
            self._gaps[0] = (retained_start, end)

    def _ensure_open(self) -> None:
        if self._closed:
            raise LiveRecordingAccessError("LIVE_SESSION_CLOSED", "实时采集访问会话已关闭。")


class LiveRecordingAccessService:
    """Owns bounded live access sessions behind the Python process boundary."""

    def __init__(self, max_sessions: int = 8) -> None:
        if max_sessions <= 0:
            raise ValueError("max_sessions must be positive")
        self._max_sessions = max_sessions
        self._sessions: dict[str, LiveRecordingAccessSession] = {}
        self._lock = threading.RLock()

    def create(
        self,
        session_id: str,
        stream_revision: str,
        schema_revision: str,
        sampling_rate_hz: float,
        channel_names: Sequence[str],
        channel_units: Sequence[str],
        capacity_samples: int,
    ) -> LiveRecordingAccessSession:
        with self._lock:
            if not session_id or session_id in self._sessions:
                raise LiveRecordingAccessError("LIVE_SESSION_ID_INVALID", "实时采集会话身份为空或已存在。")
            if len(self._sessions) >= self._max_sessions:
                raise LiveRecordingAccessError("LIVE_SESSION_CAPACITY", "实时采集访问会话容量已满。")
            session = LiveRecordingAccessSession(
                LiveSourceIdentity(session_id, stream_revision, schema_revision),
                sampling_rate_hz, channel_names, channel_units, capacity_samples,
            )
            self._sessions[session_id] = session
            return session

    def require(self, session_id: str) -> LiveRecordingAccessSession:
        with self._lock:
            try:
                return self._sessions[session_id]
            except KeyError as exc:
                raise LiveRecordingAccessError("LIVE_SESSION_NOT_FOUND", "实时采集访问会话不存在。") from exc

    def close(self, session_id: str) -> None:
        with self._lock:
            session = self._sessions.pop(session_id, None)
        if session is not None:
            session.close()

    def close_all(self) -> None:
        with self._lock:
            sessions = tuple(self._sessions.values())
            self._sessions.clear()
        for session in sessions:
            session.close()

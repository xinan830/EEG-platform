"""Identity-bound, sample-addressed access to completed EEG recordings."""

from __future__ import annotations

import hashlib
import threading
import time
from collections import OrderedDict
from concurrent.futures import Future
from dataclasses import dataclass
from pathlib import Path
from threading import Condition
from typing import Any, Callable, Sequence

import numpy as np

from app.models.recording import RecordingSummary
from app.services.recording_samples import (
    EdfSampleReader,
    SampleReader,
    WpfRecordingFactsCache,
    WpfSampleReader,
)
from app.services.sample_access_contract import SampleBlock, UnavailableSampleInterval
from app.services.wpf_recording import wpf_source_signature


class RecordingAccessError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


_CANCELLATION_CHECK_INTERVAL_S = 0.05


class _ThrottledCancellationCheck:
    """Keep fine-grained readers from turning cancellation into a DB loop."""

    def __init__(self, callback: Callable[[], None]) -> None:
        self._callback = callback
        self._last_check: float | None = None

    def __call__(self) -> None:
        now = time.monotonic()
        if self._last_check is None or now - self._last_check >= _CANCELLATION_CHECK_INTERVAL_S:
            self._callback()
            self._last_check = now

    def force(self) -> None:
        self._callback()
        self._last_check = time.monotonic()


@dataclass(frozen=True)
class RecordingSourceIdentity:
    recording_id: str
    source_sha256: str
    import_version: str
    signal_view_sha256: str | None = None


class _FileIdentityCache:
    def __init__(self, capacity: int = 8):
        self._capacity = capacity
        self._lock = threading.Lock()
        self._entries: OrderedDict[tuple[str, str, bool], tuple[tuple[int, int, int, int], str]] = OrderedDict()
        self._inflight: dict[tuple[str, str, bool], Future[tuple[tuple[int, int, int, int], str]]] = {}

    def validate(
        self, path: Path, expected_sha256: str | None, *, verify_content_hash: bool = True,
    ) -> tuple[tuple[int, int, int, int], str]:
        path = path.resolve()
        key = (str(path), expected_sha256 or "", bool(verify_content_hash))
        signature = _file_signature(path)
        with self._lock:
            cached = self._entries.get(key)
            if cached is not None and cached[0] == signature:
                self._entries.move_to_end(key)
                return cached
            future = self._inflight.get(key)
            owner = future is None
            if owner:
                future = Future()
                self._inflight[key] = future
        if not owner:
            return future.result()
        try:
            if verify_content_hash or expected_sha256 is None:
                source_sha256 = _sha256_file(path)
                if expected_sha256 is not None and source_sha256 != expected_sha256:
                    raise RecordingAccessError(
                        "RECORDING_SOURCE_IDENTITY_MISMATCH",
                        "录音文件内容与已登记的来源身份不一致。",
                    )
            else:
                # The immutable registration hash remains the source identity;
                # bounded reads still fence mutations with the file signature.
                source_sha256 = expected_sha256
            if _file_signature(path) != signature:
                raise RecordingAccessError(
                    "RECORDING_SOURCE_CHANGED_DURING_READ",
                    "读取期间录音文件发生变化，请重新登记后再分析。",
                )
            with self._lock:
                self._entries[key] = (signature, source_sha256)
                self._entries.move_to_end(key)
                while len(self._entries) > self._capacity:
                    self._entries.popitem(last=False)
            validated = (signature, source_sha256)
            future.set_result(validated)
            return validated
        except BaseException as exc:
            future.set_exception(exc)
            future.exception()
            raise
        finally:
            with self._lock:
                self._inflight.pop(key, None)


class RecordingAccessSession:
    def __init__(
        self,
        identity: RecordingSourceIdentity,
        reader: SampleReader,
        sampling_rate_hz: float,
        sample_count: int,
        channel_names: Sequence[str],
        events: Sequence[dict[str, object]],
        validate_source: Callable[[], None],
        *,
        serialize_reads: bool,
    ):
        if not np.isfinite(sampling_rate_hz) or sampling_rate_hz <= 0 or sample_count < 0:
            raise RecordingAccessError("RECORDING_METADATA_INVALID", "录音采样率或样本数无效。")
        folded = [name.casefold() for name in channel_names]
        if not channel_names or len(set(folded)) != len(folded):
            raise RecordingAccessError("RECORDING_CHANNELS_INVALID", "录音通道为空或存在不区分大小写的重名。")
        self.identity = identity
        self.sampling_rate_hz = float(sampling_rate_hz)
        self.sample_count = int(sample_count)
        self.channel_names = tuple(channel_names)
        self.events = tuple(events)
        self._reader = reader
        self._validate_source = validate_source
        self._read_lock = threading.RLock() if serialize_reads else None
        self._state = Condition()
        self._active_reads = 0
        self._closed = False

    def read_samples(
        self,
        start_sample: int,
        end_sample: int,
        channels: Sequence[str] | None = None,
        *,
        check_cancelled: Callable[[], None] | None = None,
    ) -> SampleBlock[RecordingSourceIdentity]:
        if (isinstance(start_sample, bool) or isinstance(end_sample, bool)
                or not isinstance(start_sample, int) or not isinstance(end_sample, int)
                or not 0 <= start_sample <= end_sample <= self.sample_count):
            raise RecordingAccessError("SAMPLE_RANGE_INVALID", "读取范围必须是录音内的半开样本区间。")
        requested = self.channel_names if channels is None else tuple(channels)
        if not requested:
            raise RecordingAccessError("RECORDING_CHANNELS_EMPTY", "读取至少需要一个通道。")
        if any(not isinstance(name, str) for name in requested):
            raise RecordingAccessError("RECORDING_CHANNELS_INVALID", "请求通道名称无效。")
        lookup = {name.casefold(): index for index, name in enumerate(self.channel_names)}
        try:
            indexes = [lookup[name.casefold()] for name in requested]
        except KeyError as exc:
            raise RecordingAccessError("RECORDING_CHANNEL_NOT_FOUND", f"请求通道不存在：{exc.args[0]}") from exc
        if len({name.casefold() for name in requested}) != len(requested):
            raise RecordingAccessError("RECORDING_CHANNELS_DUPLICATED", "请求通道不能重复。")
        actual_names = tuple(self.channel_names[index] for index in indexes)
        with self._state:
            if self._closed:
                raise RecordingAccessError("RECORDING_ACCESS_SESSION_CLOSED", "录音访问会话已关闭。")
            self._active_reads += 1
        try:
            reader_check = None
            if check_cancelled is not None:
                reader_check = _ThrottledCancellationCheck(check_cancelled)
                reader_check.force()
            self._validate_source()
            lock = self._read_lock
            if lock is None:
                values = self._reader.read(start_sample, end_sample, indexes, reader_check)
            else:
                with lock:
                    values = self._reader.read(start_sample, end_sample, indexes, reader_check)
            if check_cancelled is not None:
                reader_check.force()
            self._validate_source()
            source_gaps = self._reader.source_gaps(start_sample, end_sample)
        finally:
            with self._state:
                self._active_reads -= 1
                self._state.notify_all()
        values = np.asarray(values, dtype=np.float64)
        expected_shape = (end_sample - start_sample, len(actual_names))
        if values.shape != expected_shape:
            raise RecordingAccessError("SAMPLE_BLOCK_SHAPE_INVALID", "数据源返回的样本块形状与请求范围不一致。")
        return SampleBlock(
            self.identity,
            start_sample,
            end_sample,
            self.sampling_rate_hz,
            actual_names,
            ("V",) * len(actual_names),
            values,
            _unavailable_intervals(values, actual_names, start_sample, source_gaps),
        )

    def close(self) -> None:
        with self._state:
            if self._closed:
                return
            self._closed = True
            while self._active_reads:
                self._state.wait()
        self._reader.close()

    def __enter__(self) -> RecordingAccessSession:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


class RecordingAccessService:
    def __init__(self, storage_dir: Path, wpf_facts: WpfRecordingFactsCache, montage_snapshots: Any):
        self._storage_dir = Path(storage_dir)
        self._wpf_facts = wpf_facts
        self._montage_snapshots = montage_snapshots
        self._file_identities = _FileIdentityCache()

    def open_session(
        self,
        recording: RecordingSummary,
        *,
        analysis_montage_id: str | None = None,
        verify_content_hash: bool = True,
    ) -> RecordingAccessSession:
        if recording.extension == ".wpf":
            return self._open_wpf(recording, analysis_montage_id)
        return self._open_edf(recording, verify_content_hash=verify_content_hash)

    def _open_wpf(self, recording: RecordingSummary, montage_id: str | None) -> RecordingAccessSession:
        if recording.stored_name.startswith(("wpf:", "wpf-montage:")):
            directory_name = recording.stored_name.split(":", 1)[1]
        else:
            directory_name = recording.stored_name
        root = Path(directory_name).expanduser().resolve()
        facts = self._wpf_facts.get(root, recording.source_sha256)
        reader = WpfSampleReader(facts)
        signal_view_sha256 = None
        if recording.stored_name.startswith("wpf-montage:"):
            from app.services.acquisition_analysis_signals import AcquisitionMontageSampleReader

            view = self._montage_snapshots.resolve(
                recording.id, root, montage_id or recording.selected_montage_id,
            )
            reader = AcquisitionMontageSampleReader(reader, view)
            signal_view_sha256 = str(view.provenance.get("snapshot_sha256", "")) or None
        identity = RecordingSourceIdentity(
            recording.id, facts.source_sha256, recording.import_version, signal_view_sha256,
        )

        def validate_source() -> None:
            if wpf_source_signature(root) != facts.source_signature:
                raise RecordingAccessError("RECORDING_SOURCE_CHANGED", "WPF 录音源已变化，访问会话已失效。")

        return RecordingAccessSession(
            identity, reader, facts.sampling_rate_hz, facts.sample_count, reader.names, (),
            validate_source, serialize_reads=False,
        )

    def _open_edf(self, recording: RecordingSummary, *, verify_content_hash: bool) -> RecordingAccessSession:
        import mne

        path = (self._storage_dir / recording.stored_name).resolve()
        if recording.extension not in {".bdf", ".edf"}:
            raise RecordingAccessError("RECORDING_FORMAT_UNSUPPORTED", "不支持此录音格式。")
        signature, source_sha256 = self._file_identities.validate(
            path, recording.source_sha256, verify_content_hash=verify_content_hash,
        )
        reader_factory = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader_factory(path, preload=False, verbose=False)
        try:
            sfreq = float(raw.info["sfreq"])
            channel_names = tuple(raw.ch_names)
            sample_count = int(raw.n_times)
            events = tuple(
                {"elapsed_s": float(onset), "label": str(label)}
                for onset, label in zip(raw.annotations.onset, raw.annotations.description)
            )
            identity = RecordingSourceIdentity(
                recording.id,
                source_sha256,
                recording.import_version,
            )
            reader = EdfSampleReader(raw)
            session = RecordingAccessSession(
                identity, reader, sfreq, sample_count, channel_names, events,
                lambda: _ensure_file_signature(path, signature), serialize_reads=True,
            )
        except BaseException:
            raw.close()
            raise
        return session


def _file_signature(path: Path) -> tuple[int, int, int, int]:
    try:
        stat = path.stat()
    except OSError as exc:
        raise RecordingAccessError("RECORDING_SOURCE_UNAVAILABLE", "录音文件无法读取。") from exc
    return stat.st_size, stat.st_mtime_ns, getattr(stat, "st_ino", 0), getattr(stat, "st_dev", 0)


def _ensure_file_signature(path: Path, expected: tuple[int, int, int, int]) -> None:
    if _file_signature(path) != expected:
        raise RecordingAccessError("RECORDING_SOURCE_CHANGED", "录音文件已变化，访问会话已失效。")


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exc:
        raise RecordingAccessError("RECORDING_SOURCE_UNAVAILABLE", "录音文件无法读取。") from exc
    return digest.hexdigest()


def _unavailable_intervals(
    values: np.ndarray,
    channels: Sequence[str],
    start_sample: int,
    source_gaps: Sequence[range],
) -> tuple[UnavailableSampleInterval, ...]:
    intervals: list[UnavailableSampleInterval] = []
    source_gap_mask = np.zeros(values.shape[0], dtype=bool) if source_gaps else None
    for gap in source_gaps:
        left, right = gap.start - start_sample, gap.stop - start_sample
        if gap.step != 1 or left < 0 or right > len(values) or right <= left:
            raise RecordingAccessError("SOURCE_GAP_RANGE_INVALID", "数据源缺口超出当前样本块范围。")
        source_gap_mask[left:right] = True
        intervals.append(UnavailableSampleInterval(gap.start, gap.stop, "source_gap", tuple(channels)))
    for channel_index, channel in enumerate(channels):
        missing = ~np.isfinite(values[:, channel_index])
        if source_gap_mask is not None:
            missing &= ~source_gap_mask
        edges = np.diff(np.pad(missing, (1, 1)).astype(np.int8))
        starts, ends = np.flatnonzero(edges == 1), np.flatnonzero(edges == -1)
        intervals.extend(
            UnavailableSampleInterval(
                start_sample + int(left), start_sample + int(right), "missing_value", (channel,),
            )
            for left, right in zip(starts, ends)
        )
    return tuple(intervals)

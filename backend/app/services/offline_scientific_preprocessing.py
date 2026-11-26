"""Unified offline scientific preprocessing service for Review and standalone analyses.

Explicitly separates two scientific preparation contracts under one facade:
- Continuous full-recording preparation (delegated to ContinuousPreprocessingService + AnalysisPreprocessCache)
- Bounded finite-context window preparation (wrapped with a bounded thread-safe LRU + single-flight cache)

Display waveform filtering remains completely decoupled and never feeds this layer.
"""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import replace
from concurrent.futures import Future
from threading import RLock
from typing import Any

from app.models.recording import RecordingSummary
from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.scientific.quality import SpectralQualityGateError
from app.services.analysis_preprocess_cache import PreprocessedRecording
from app.services.bounded_window_preprocessing import PreparedWindow, prepare_window
from app.services.window_artifact_qc import apply_window_artifact_qc
from app.services.continuous_preprocessing import ContinuousPreprocessingService, PreprocessingRecordingAccess
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache
from app.services.ica_review_cache import IcaReviewCache
from app.services.recording_access import RecordingAccessSession


class OfflineScientificPreprocessingService:
    """Owns offline scientific preprocessing coordination and bounded window caching."""

    def __init__(
        self,
        continuous: ContinuousPreprocessingService,
        *,
        max_window_entries: int = 8,
        max_window_bytes: int = 32 * 1024 * 1024,
    ) -> None:
        if max_window_entries < 1 or max_window_bytes < 1:
            raise ValueError("离线窗口预处理缓存容量必须大于零")
        self._continuous = continuous
        self._max_window_entries = int(max_window_entries)
        self._max_window_bytes = int(max_window_bytes)
        self._window_entries: OrderedDict[tuple, PreparedWindow] = OrderedDict()
        self._window_inflight: dict[tuple, Future[PreparedWindow]] = {}
        self._global_epoch = 0
        self._recording_epochs: dict[str, int] = {}
        self._lock = RLock()
        self._window_hits = 0
        self._window_misses = 0
        self._window_coalesced = 0

    @classmethod
    def for_recording_access(cls, open_access_session, analysis_signal_provenance, storage_dir=None):
        from pathlib import Path
        ica_store = (Path(storage_dir) / ".ica-review-models") if storage_dir is not None else None
        access = PreprocessingRecordingAccess(
            open_access_session, analysis_signal_provenance, IcaReviewCache(storage_dir=ica_store), storage_dir,
        )
        return cls(ContinuousPreprocessingService(access, AnalysisPreprocessCache()))

    @property
    def prepared_cache(self):
        return self._continuous.preprocess_cache

    @property
    def ica_review_cache(self):
        return self._continuous.recordings.ica_review_cache

    def prepare_continuous(
        self,
        recording: RecordingSummary,
        *,
        low_hz: float,
        high_hz: float,
        notch_hz: float | None,
        execution_context: Any = None,
        channels: list[str] | None = None,
        requested_channels_only: bool = False,
        bad_channel_spec: BadChannelSpec | None = None,
        artifact_rejection_spec: ArtifactRejectionSpec | None = None,
        artifact_removal_spec: ArtifactRemovalSpec | None = None,
        preprocessing_spec: OfflinePreprocessingSpec | None = None,
        artifact_reference_channels: tuple[str, ...] = (),
    ) -> PreprocessedRecording:
        """Full-recording continuous scientific preparation driven by OfflinePreprocessingSpec."""
        resolved_spec = preprocessing_spec or OfflinePreprocessingSpec(
            filter_spec=ScientificFilterSpec(
                low_hz=float(low_hz),
                high_hz=float(high_hz),
                notch_hz=None if notch_hz is None else float(notch_hz),
                filter_phase="zero_phase",
                scope="whole_recording_before_window_slice",
            ),
            bad_channels=(bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()),
            artifact_rejection=(
                artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
            ),
            artifact_removal=(
                artifact_removal_spec.normalized() if artifact_removal_spec is not None else ArtifactRemovalSpec()
            ),
        )
        if recording.sfreq is not None:
            resolved_spec = resolved_spec.validate(float(recording.sfreq))
        else:
            resolved_spec = OfflinePreprocessingSpec(
                filter_spec=resolved_spec.filter_spec,
                bad_channels=resolved_spec.bad_channels.normalized(),
                artifact_rejection=resolved_spec.artifact_rejection.normalized(),
                artifact_removal=resolved_spec.artifact_removal.normalized(),
            )
        return self._continuous.load(
            recording,
            resolved_spec,
            execution_context,
            channels=channels,
            requested_channels_only=requested_channels_only,
            _artifact_reference_channels=artifact_reference_channels,
        )

    def prepare_bounded_window(
        self,
        session: RecordingAccessSession,
        *,
        start_sample: int,
        end_sample: int,
        channels: list[str] | None,
        low_hz: float,
        high_hz: float,
        notch_hz: float | None,
        context_s: float,
        algorithm_version: str,
        execution_context: Any = None,
        artifact_rejection_spec: ArtifactRejectionSpec | None = None,
        preprocessing_spec: OfflinePreprocessingSpec | None = None,
        recording: RecordingSummary | None = None,
    ) -> PreparedWindow:
        """Finite-context zero-phase window preparation driven by OfflinePreprocessingSpec."""
        if execution_context is not None:
            execution_context.check_cancelled()

        session_bad = getattr(session, "applied_bad_channel_spec", BadChannelSpec()).normalized()
        if preprocessing_spec is not None:
            resolved_spec = preprocessing_spec.validate(float(session.sampling_rate_hz))
            if resolved_spec.bad_channels.fingerprint() != session_bad.fingerprint():
                raise ValueError(
                    "BAD_CHANNEL_SPEC_SESSION_MISMATCH: 有界窗口预处理请求的坏导策略与访问会话实际应用的坏导策略不一致"
                )
            if resolved_spec.artifact_removal.is_active:
                raise ValueError(
                    "ARTIFACT_REMOVAL_REQUIRES_CONTINUOUS_SCOPE: 离线去伪迹（EOG/SSP/ICA）需要整段连续预处理上下文，不支持在有界局部窗口中独立拟合"
                )
        else:
            resolved_spec = OfflinePreprocessingSpec(
                filter_spec=ScientificFilterSpec(
                    low_hz=float(low_hz),
                    high_hz=float(high_hz),
                    notch_hz=None if notch_hz is None else float(notch_hz),
                    filter_phase="zero_phase",
                    scope="bounded_window_with_finite_context",
                ),
                bad_channels=session_bad,
                artifact_rejection=(
                    artifact_rejection_spec.normalized()
                    if artifact_rejection_spec is not None
                    else ArtifactRejectionSpec()
                ),
            ).validate(float(session.sampling_rate_hz))
        low_hz = resolved_spec.filter_spec.low_hz
        high_hz = resolved_spec.filter_spec.high_hz
        notch_hz = resolved_spec.filter_spec.notch_hz
        notch_q = resolved_spec.filter_spec.notch_q
        if resolved_spec.artifact_rejection.is_active:
            if recording is None:
                raise ValueError('ARTIFACT_QC_RECORDING_REQUIRED: 有界坏段质控需要完整录音上下文')
            if (recording.id != session.identity.recording_id
                    or recording.source_sha256 != session.identity.source_sha256
                    or recording.import_version != session.identity.import_version):
                raise ValueError('ARTIFACT_REPORT_SOURCE_MISMATCH: 坏段报告与窗口输入不一致')
        key = self._window_cache_key(
            session,
            start_sample=start_sample,
            end_sample=end_sample,
            channels=channels,
            spec=resolved_spec,
            context_s=context_s,
            algorithm_version=algorithm_version,
        )
        if key is None:
            prepared = prepare_window(
                session, start_sample=start_sample, end_sample=end_sample, channels=channels,
                low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, notch_q=notch_q, context_s=context_s,
                algorithm_version=algorithm_version, execution_context=execution_context,
            )
            self._apply_window_qc(prepared, resolved_spec, recording, session, start_sample, end_sample, execution_context)
            prepared.evidence["offline_preprocessing_spec"] = resolved_spec.to_evidence()
            prepared.data.flags.writeable = False
            return prepared

        recording_id = key[0]
        while True:
            if execution_context is not None:
                execution_context.check_cancelled()
            leader = False
            future: Future[PreparedWindow] | None = None
            start_global_epoch = 0
            start_recording_epoch = 0
            with self._lock:
                cached = self._window_entries.get(key)
                if cached is not None:
                    self._window_entries.move_to_end(key)
                    self._window_hits += 1
                    return cached
                existing = self._window_inflight.get(key)
                if existing is not None:
                    self._window_coalesced += 1
                    future = existing
                else:
                    future = Future()
                    self._window_inflight[key] = future
                    self._window_misses += 1
                    start_global_epoch = self._global_epoch
                    start_recording_epoch = self._recording_epochs.get(recording_id, 0)
                    leader = True

            if not leader:
                try:
                    result = future.result()
                except _LeaderCancelled:
                    continue
                if execution_context is not None:
                    execution_context.check_cancelled()
                return result

            try:
                prepared = prepare_window(
                    session, start_sample=start_sample, end_sample=end_sample, channels=channels,
                    low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, notch_q=notch_q, context_s=context_s,
                    algorithm_version=algorithm_version, execution_context=execution_context,
                )
                self._apply_window_qc(prepared, resolved_spec, recording, session, start_sample, end_sample, execution_context)
                prepared.evidence["offline_preprocessing_spec"] = resolved_spec.to_evidence()
                prepared.data.flags.writeable = False
            except Exception as exc:
                with self._lock:
                    if self._window_inflight.get(key) is future:
                        self._window_inflight.pop(key, None)
                if execution_context is not None and not isinstance(exc, SpectralQualityGateError):
                    future.set_exception(_LeaderCancelled())
                else:
                    future.set_exception(exc)
                raise

            with self._lock:
                if self._window_inflight.get(key) is future:
                    self._window_inflight.pop(key, None)
                if (
                    self._global_epoch == start_global_epoch
                    and self._recording_epochs.get(recording_id, 0) == start_recording_epoch
                ):
                    self._window_entries[key] = prepared
                    self._window_entries.move_to_end(key)
                    while (
                        len(self._window_entries) > self._max_window_entries
                        or sum(item.data.nbytes for item in self._window_entries.values()) > self._max_window_bytes
                    ):
                        self._window_entries.popitem(last=False)
            future.set_result(prepared)
            return prepared

    def _apply_window_qc(
        self, prepared: PreparedWindow, spec: OfflinePreprocessingSpec,
        recording: RecordingSummary | None, session: RecordingAccessSession,
        start_sample: int, end_sample: int, execution_context: Any,
    ) -> None:
        if not spec.artifact_rejection.is_active:
            return
        if recording is None:
            raise ValueError('ARTIFACT_QC_RECORDING_REQUIRED: 有界坏段质控需要完整录音上下文')
        annotated = self.prepare_continuous(
            recording, low_hz=spec.filter_spec.low_hz, high_hz=spec.filter_spec.high_hz,
            notch_hz=spec.filter_spec.notch_hz, execution_context=execution_context,
            preprocessing_spec=replace(spec,
                filter_spec=replace(spec.filter_spec, scope='whole_recording_before_window_slice'),
                artifact_rejection=replace(spec.artifact_rejection, policy='annotate')),
        )
        if (annotated.sfreq != session.sampling_rate_hz or len(annotated.data) != session.sample_count
                or annotated.channel_names != tuple(session.channel_names)
                or annotated.source_identity != session.identity):
            raise ValueError('ARTIFACT_REPORT_SOURCE_MISMATCH: 坏段报告与窗口输入不一致')
        apply_window_artifact_qc(prepared, spec, start_sample=start_sample, end_sample=end_sample,
                                 annotated_source=annotated)

    def invalidate_recording(self, recording_id: str) -> int:
        self._continuous.invalidate_recording(recording_id)
        with self._lock:
            self._recording_epochs[recording_id] = self._recording_epochs.get(recording_id, 0) + 1
            removed = [key for key in self._window_entries if key[0] == recording_id]
            for key in removed:
                self._window_entries.pop(key, None)
            for key in [k for k in self._window_inflight if k[0] == recording_id]:
                self._window_inflight.pop(key, None)
            return len(removed)

    def clear_window_cache(self) -> None:
        with self._lock:
            self._global_epoch += 1
            self._window_entries.clear()
            self._window_inflight.clear()

    def window_cache_stats(self) -> dict[str, int]:
        with self._lock:
            return {
                "hits": self._window_hits,
                "misses": self._window_misses,
                "coalesced": self._window_coalesced,
                "entries": len(self._window_entries),
            }

    @staticmethod
    def _window_cache_key(
        session: RecordingAccessSession,
        *,
        start_sample: int,
        end_sample: int,
        channels: list[str] | None,
        spec: OfflinePreprocessingSpec,
        context_s: float,
        algorithm_version: str,
    ) -> tuple | None:
        identity = getattr(session, "identity", None)
        recording_id = getattr(identity, "recording_id", None)
        source_sha256 = getattr(identity, "source_sha256", None)
        import_version = getattr(identity, "import_version", None)
        signal_view_sha256 = getattr(identity, "signal_view_sha256", None)
        if not recording_id or not source_sha256:
            return None
        lookup = {name.casefold(): name for name in session.channel_names}
        try:
            canonical = tuple(
                session.channel_names if channels is None else (lookup[name.casefold()] for name in channels)
            )
        except KeyError:
            return None
        return (
            str(recording_id),
            str(source_sha256),
            str(import_version or ""),
            str(signal_view_sha256 or ""),
            int(start_sample),
            int(end_sample),
            canonical,
            round(float(session.sampling_rate_hz), 6),
            spec.fingerprint(),
            round(float(context_s), 6),
            str(algorithm_version),
        )


class _LeaderCancelled(Exception):
    """Internal signal allowing coalesced waiters to retry when a leader was cancelled."""

"""Bounded asynchronous scheduling for acquisition-scoped window algorithms."""

from __future__ import annotations

import logging
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
from threading import BoundedSemaphore, RLock

from pydantic import BaseModel, JsonValue, ValidationError

from app.services.live_analysis.live_analysis_contracts import (
    LiveAnalysisAlgorithm, LiveAnalysisAlgorithmRegistry, LiveAnalysisEnvelope, LiveWindowPlan,
)
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessSession
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService


logger = logging.getLogger(__name__)


@dataclass
class _LiveAlgorithmSession:
    algorithm: LiveAnalysisAlgorithm
    source: LiveRecordingAccessSession
    config: BaseModel
    plan: LiveWindowPlan
    montage_revision: int
    first_sample: int | None
    next_endpoint: int | None
    latest: LiveAnalysisEnvelope
    revision: int = 0
    delivered_revision: int = -1
    future: Future | None = None


class LiveAnalysisCoordinator:
    """Owns scheduling and latest results, never device capture or offline Runs.

    One in-flight window per algorithm and a fixed global worker budget prevent
    slow algorithms from creating an unbounded executor queue. Stop invalidates
    running work; scientific calls already running may finish but cannot publish.
    """

    def __init__(
        self,
        registry: LiveAnalysisAlgorithmRegistry,
        max_workers: int = 4,
        preprocessor: LiveScientificPreprocessingService | None = None,
    ) -> None:
        if max_workers <= 0:
            raise ValueError("max_workers must be positive")
        self._registry = registry
        self._preprocessor = preprocessor
        self._sessions: dict[tuple[str, str], _LiveAlgorithmSession] = {}
        self._lock = RLock()
        self._workers = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="live-analysis")
        self._slots = BoundedSemaphore(max_workers)
        self._schedule_cursor = 0
        self._closed = False

    @property
    def preprocessor(self) -> LiveScientificPreprocessingService | None:
        return self._preprocessor

    def start(
        self, session_id: str, source: LiveRecordingAccessSession,
        algorithm_id: str, config: dict[str, JsonValue],
    ) -> LiveAnalysisEnvelope:
        self._check_source(session_id, source)
        algorithm = self._registry.resolve(algorithm_id)
        try:
            normalized, plan = algorithm.configure(source, config)
        except ValidationError as exc:
            raise LiveRecordingAccessError("LIVE_CONFIG_INVALID", str(exc)) from exc
        if plan.window_samples <= 0 or plan.step_samples <= 0 or not plan.channels:
            raise LiveRecordingAccessError("LIVE_WINDOW_PLAN_INVALID", "实时算法窗口配置无效。")
        if plan.window_samples > source.capacity_samples:
            raise LiveRecordingAccessError("LIVE_WINDOW_EXCEEDS_RETENTION", "算法窗口超过实时样本保留容量。")
        montage_revision = source.montage_revision
        warming = LiveAnalysisEnvelope(
            acquisition_session_id=session_id, algorithm_id=algorithm.algorithm_id,
            revision=0, state="warming_up",
            montage_revision=montage_revision,
            scientific_identity=getattr(algorithm, "scientific_identity", None),
            implementation_identity=getattr(algorithm, "implementation_identity", None),
        )
        with self._lock:
            if self._closed:
                raise LiveRecordingAccessError("LIVE_COORDINATOR_CLOSED", "实时分析服务已关闭。")
            high = source.range_snapshot().high_watermark_sample
            first = source.first_sample if high is not None else None
            endpoint = max(first + plan.window_samples, high + 1) if first is not None and high is not None else None
            active = _LiveAlgorithmSession(
                algorithm, source, normalized, plan, montage_revision, first, endpoint, warming,
            )
            self.stop(session_id, algorithm_id)
            self._sessions[(session_id, algorithm.algorithm_id.casefold())] = active
            if high is not None:
                self._schedule(active, high + 1)
            return active.latest

    def stop(self, session_id: str, algorithm_id: str) -> None:
        with self._lock:
            active = self._sessions.pop((session_id, algorithm_id.casefold()), None)
            if active is not None and active.future is not None:
                active.future.cancel()

    def close_session(self, session_id: str) -> None:
        with self._lock:
            for key in list(self._sessions):
                if key[0] == session_id:
                    self.stop(*key)
            if self._preprocessor is not None:
                self._preprocessor.invalidate_session(session_id)

    def close_all(self) -> None:
        with self._lock:
            for key in list(self._sessions):
                self.stop(*key)
            if self._preprocessor is not None:
                self._preprocessor.clear()

    def shutdown(self) -> None:
        with self._lock:
            self._closed = True
            self.close_all()
        self._workers.shutdown(wait=False, cancel_futures=True)

    def latest(self, session_id: str, algorithm_id: str) -> LiveAnalysisEnvelope | None:
        with self._lock:
            active = self._sessions.get((session_id, algorithm_id.casefold()))
            return active.latest if active else None

    def on_samples(self, session_id: str, source: LiveRecordingAccessSession) -> list[LiveAnalysisEnvelope]:
        """Inspect sample endpoints and submit bounded work without awaiting science."""
        self._check_source(session_id, source)
        with self._lock:
            active_sessions = [active for key, active in self._sessions.items() if key[0] == session_id]
            if any(active.source is not source for active in active_sessions):
                raise LiveRecordingAccessError("LIVE_SESSION_SOURCE_MISMATCH", "实时分析数据源已被替换。")
            high = source.range_snapshot().high_watermark_sample if active_sessions else None
            if high is None:
                return []
            changed: list[LiveAnalysisEnvelope] = []
            count = len(active_sessions)
            start_index = self._schedule_cursor % count if count else 0
            if count:
                self._schedule_cursor = (start_index + 1) % count
            ordered = active_sessions[start_index:] + active_sessions[:start_index]
            for active in ordered:
                self._schedule(active, high + 1)
            for active in active_sessions:
                if active.latest.revision > active.delivered_revision:
                    active.delivered_revision = active.latest.revision
                    changed.append(active.latest)
            return changed

    def _schedule(self, active: _LiveAlgorithmSession, available_end: int) -> None:
        bounds = self._reserve_window(active, available_end)
        if bounds is None:
            return
        start, end, skipped = bounds
        if active.future is not None or not self._slots.acquire(blocking=False):
            self._publish(active, self._envelope(active, start, end, state="overloaded",
                skipped_windows=skipped + 1,
                failure={"code": "LIVE_ANALYSIS_OVERLOADED", "message": "实时分析繁忙，当前窗口已跳过。"}))
            return
        try:
            active.future = self._workers.submit(self._calculate, active, start, end, skipped, active.revision)
        except RuntimeError:
            self._slots.release()
            raise
        active.future.add_done_callback(lambda future: self._complete(active, future))

    def _calculate(self, active: _LiveAlgorithmSession, start: int, end: int, skipped: int, revision: int) -> LiveAnalysisEnvelope:
        try:
            block = active.source.read_samples(start, end, active.plan.channels)
            if block.unavailable_intervals:
                return self._envelope(active, start, end, revision=revision, state="unavailable", skipped_windows=skipped,
                    failure={"code": "LIVE_ANALYSIS_WINDOW_UNAVAILABLE",
                        "message": "当前实时窗口包含缺失或已覆盖的样本。",
                        "reasons": sorted({item.reason for item in block.unavailable_intervals})})
            result = active.algorithm.calculate(
                active.source.identity.acquisition_session_id, block, active.config, active.first_sample,
            ).model_dump(mode="json")
            return self._envelope(active, start, end, revision=revision, state=result["state"], skipped_windows=skipped,
                scientific_identity=result.get("scientific_identity"),
                implementation_identity=result.get("implementation_identity"),
                payload=result, failure=result.get("failure"))
        except Exception as exc:
            logger.exception("Live algorithm %s failed for session %s", active.algorithm.algorithm_id,
                active.source.identity.acquisition_session_id)
            code = exc.code if isinstance(exc, LiveRecordingAccessError) else "LIVE_ANALYSIS_ALGORITHM_FAILED"
            return self._envelope(active, start, end, revision=revision, state="unavailable", skipped_windows=skipped,
                failure={"code": code, "message": "实时算法未能处理当前窗口。"})

    def _complete(self, active: _LiveAlgorithmSession, future: Future) -> None:
        with self._lock:
            active.future = None
            self._slots.release()
            if not future.cancelled():
                self._publish(active, future.result())

    def _publish(self, active: _LiveAlgorithmSession, update: LiveAnalysisEnvelope) -> None:
        key = (active.source.identity.acquisition_session_id, active.algorithm.algorithm_id.casefold())
        if self._sessions.get(key) is active and update.revision > active.latest.revision:
            active.latest = update

    @staticmethod
    def _check_source(session_id: str, source: LiveRecordingAccessSession) -> None:
        if source.identity.acquisition_session_id != session_id:
            raise LiveRecordingAccessError("LIVE_SESSION_ID_MISMATCH", "实时分析会话身份不匹配。")

    @staticmethod
    def _reserve_window(active: _LiveAlgorithmSession, available_end: int) -> tuple[int, int, int] | None:
        if active.first_sample is None:
            active.first_sample = active.source.first_sample
            if active.first_sample is None:
                return None
            active.next_endpoint = active.first_sample + active.plan.window_samples
        endpoint = active.next_endpoint
        if endpoint is None or available_end < endpoint:
            return None
        skipped = (available_end - endpoint) // active.plan.step_samples
        endpoint += skipped * active.plan.step_samples
        active.next_endpoint = endpoint + active.plan.step_samples
        active.revision += 1
        return endpoint - active.plan.window_samples, endpoint, skipped

    @staticmethod
    def _envelope(active: _LiveAlgorithmSession, start: int, end: int, **fields) -> LiveAnalysisEnvelope:
        fields.setdefault("scientific_identity", getattr(active.algorithm, "scientific_identity", None))
        fields.setdefault("implementation_identity", getattr(active.algorithm, "implementation_identity", None))
        fields.setdefault("montage_revision", active.montage_revision)
        return LiveAnalysisEnvelope(
            acquisition_session_id=active.source.identity.acquisition_session_id,
            algorithm_id=active.algorithm.algorithm_id, revision=fields.pop("revision", active.revision),
            start_sample=start, end_sample=end,
            start_s=(start - active.first_sample) / active.source.sampling_rate_hz,
            end_s=(end - active.first_sample) / active.source.sampling_rate_hz,
            **fields,
        )

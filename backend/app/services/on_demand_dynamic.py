"""Generic service for backend-planned single dynamic-analysis windows."""

from __future__ import annotations

import logging
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any
from uuid import uuid4

from app.algorithm_runtime.on_demand import (
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowRequest,
    DynamicWindowResult,
)
from app.algorithm_runtime.errors import (
    DynamicWindowCapacityExceededError, DynamicWindowResultCorruptError,
)
from app.algorithm_runtime.on_demand_registry import DynamicWindowCapabilityRegistry
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import sha256_json
from app.core.performance_trace import measure_performance, performance_trace
from app.algorithm_runtime.offline_execution import (
    OFFLINE_EXECUTION_CONTRACT_VERSION,
)
from app.persistence.repositories.dynamic_window import DynamicWindowRecord, DynamicWindowRepository
from app.services.recordings import RecordingService
from app.services.on_demand_worker import execute_window_job
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore
from app.services.dynamic_window_identity import DynamicWindowIdentityResolver
from app.services.dynamic_window_planning import DynamicWindowPlanner, PreparedDynamicWindow
from app.services.projects import ProjectService
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator
from app.services.tnk_playback_delivery import TnkPlaybackDeliveryService
from app.services.dynamic_window_responses import (
    plan_from_records, response, session_cache_key, session_scientific_configuration_json,
)


class DynamicWindowService:
    """Own identity, bounded execution, publication, and status serialization."""

    def __init__(self, recordings: RecordingService, registry: AlgorithmRegistry,
                 database_path: Path = DATABASE_PATH, artifacts_dir: Path = ARTIFACTS_DIR,
                 *, enabled_algorithms: set[str] | None = None,
                 max_pending_windows: int = 16,
                 resource_policy: DynamicWindowResourcePolicy | None = None,
                 compute_coordinator: ScientificComputeCoordinator | None = None) -> None:
        if type(max_pending_windows) is not int or max_pending_windows < 1:
            raise ValueError("max_pending_windows must be a positive integer")
        self.recordings = recordings
        self.resource_policy = resource_policy or DynamicWindowResourcePolicy()
        self.compute_coordinator = compute_coordinator or ScientificComputeCoordinator()
        self.registry = registry
        declared = DynamicWindowCapabilityRegistry.from_algorithm_registry(registry)
        self.capabilities = DynamicWindowCapabilityRegistry([
            item for item in declared.list()
            if enabled_algorithms is None or item.manifest.algorithm_id in enabled_algorithms
        ])
        self.repository = DynamicWindowRepository(database_path)
        self._projects = ProjectService(database_path)
        self._identity_resolver = DynamicWindowIdentityResolver(database_path)
        self._planner = DynamicWindowPlanner(recordings, registry, self.capabilities, self.repository,
                                             self._projects, self._identity_resolver)
        self.artifacts_dir = Path(artifacts_dir) / "dynamic-windows"
        self.artifacts_dir.mkdir(parents=True, exist_ok=True)
        # Keep admission and cache-hit timings in the application log stream.
        self._logger = logging.getLogger("brain_platform.dynamic_window")
        self._artifact_store = DynamicWindowArtifactStore(self.artifacts_dir, self.repository)
        self._artifact_store.cleanup()
        self._executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="dynamic-window")
        self._lock = threading.RLock()
        self._pending: dict[str, Any] = {}
        self.playback_delivery = TnkPlaybackDeliveryService(self.repository, self._logger)
        self._max_pending_windows = max_pending_windows
        self._stop = threading.Event()
        self._recovery_thread = threading.Thread(target=self._recover_loop, name="window-recovery", daemon=True)
        self._recovery_thread.start()

    def close(self, *, wait: bool = False) -> None:
        """Stop admission/recovery; optionally join cooperative worker cleanup."""
        with self._lock:
            self._stop.set()
            self._executor.shutdown(wait=False, cancel_futures=True)
        self._recovery_thread.join(timeout=5)
        if wait:
            # Do not hold the gate: execution cleanup removes its pending entry.
            self._executor.shutdown(wait=True, cancel_futures=True)
        self._artifact_store.close()

    def ensure(self, payload: DynamicWindowRequest) -> dict[str, Any]:
        return self._ensure(payload)

    def _ensure(self, payload: DynamicWindowRequest, *, recovering: DynamicWindowRecord | None = None) -> dict[str, Any]:
        with performance_trace("dynamic_window_admission_perf",
                request_id=payload.request_id, algorithm_id=payload.algorithm_id,
                recording_id=payload.recording_id, cursor_sample=payload.cursor_sample) as trace:
            if self._stop.is_set():
                raise DynamicWindowCapacityExceededError("动态分析服务正在关闭。")
            with measure_performance("request_planning"):
                prepared = self._planner.prepare(payload, recovering=recovering)
            waiting = time.perf_counter()
            with self._lock:
                trace.add("lock_wait", (time.perf_counter() - waiting) * 1000)
                held = time.perf_counter()
                try:
                    if self._stop.is_set():
                        if hasattr(prepared.inputs.payload, "close"):
                            prepared.inputs.payload.close()
                        raise DynamicWindowCapacityExceededError("动态分析服务正在关闭。")
                    value = self._ensure_locked(payload, prepared, recovering=recovering)
                    trace.identity.update(window_id=value["window_id"], state=value["state"])
                    return value
                finally:
                    trace.add("lock_held", (time.perf_counter() - held) * 1000)

    def _ensure_locked(self, payload: DynamicWindowRequest, prepared: PreparedDynamicWindow, *, recovering: DynamicWindowRecord | None = None) -> dict[str, Any]:
        session, capability = prepared.session, prepared.capability
        typed_config, inputs, plan = prepared.config, prepared.inputs, prepared.plan
        session_identity = plan.session_identity
        started, planning_ms = prepared.started, prepared.planning_ms
        input_resolution_ms = prepared.input_resolution_ms
        window_planning_ms = prepared.window_planning_ms

        window = self.repository.ensure_window(
            session.session_id, start_sample=plan.start_sample, end_sample=plan.end_sample,
            start_s=plan.start_s, end_s=plan.end_s,
        )
        # ``ensure_window`` returns the newest generation for this scientific
        # range.  A terminal cancelled/failed generation is immutable; an
        # explicit retry creates a new row instead of reopening it.
        if payload.retry:
            window = self.repository.retry_window(
                session.session_id, start_sample=plan.start_sample, end_sample=plan.end_sample,
                start_s=plan.start_s, end_s=plan.end_s,
                retry_of_window_id=payload.retry_of_window_id,
            )
        identity = DynamicWindowIdentity(
            **session_identity.model_dump(), generation=window.generation,
            start_sample=plan.start_sample, end_sample=plan.end_sample,
        )
        delivery_state, frame_publisher = self.playback_delivery.prepare(
            payload.playback_delivery, payload.playback_delivery_id or uuid4().hex,
            session.session_id, identity, inputs.sfreq_hz,
            capability, window.window_id,
        )
        response_delivery = delivery_state
        request_id = payload.request_id or uuid4().hex
        if recovering is not None and window.window_id != recovering.window_id:
            raise ValueError("recovery resolved a different window generation")
        if window.state in {"ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"}:
            result = self._read_artifact(window.result_artifact_id) if window.state == "ready" else None
            subscription_id = self.repository.create_subscription(
                window.window_id, request_id, project_id=payload.project_id,
            ).subscription_id
            self._logger.info(
                "dynamic_window_cache_hit algorithm_id=%s recording_id=%s request_id=%s window_id=%s target_samples=%s:%s context_samples=%s:%s state=%s admission_ms=%.1f input_resolution_ms=%.1f window_planning_ms=%.1f",
                identity.algorithm_id, identity.recording_id, request_id, window.window_id,
                plan.start_sample, plan.end_sample, plan.context_start_sample, plan.context_end_sample,
                window.state, (time.perf_counter() - started) * 1000,
                input_resolution_ms, window_planning_ms,
            )
            if delivery_state is not None and delivery_state["state"] == "enabled" and result is not None:
                frame_publisher(identity, DynamicWindowResult.model_validate(result))
            return response(session.session_id, window, plan, request_id, result=result, subscription_id=subscription_id,
                                  scientific_cache_key=session_cache_key(session),
                                  scientific_configuration_json=session_scientific_configuration_json(session),
                                  playback_delivery=response_delivery)
        if plan.state != "planned":
            window = self.repository.transition_window(
                window.window_id, "insufficient_data", quality="insufficient_data",
                failure={"code": "WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window is not yet available"},
            )
            subscription_id = self.repository.create_subscription(
                window.window_id, request_id, project_id=payload.project_id,
            ).subscription_id
            return response(session.session_id, window, plan, request_id, subscription_id=subscription_id,
                                  scientific_cache_key=session_cache_key(session),
                                  scientific_configuration_json=session_scientific_configuration_json(session),
                                  playback_delivery=response_delivery)
        # Fence leases left by a crashed process. The current ensure request
        # supplies the immutable inputs needed to submit the recovery attempt.
        self.repository.recover_expired_attempts()
        attempt_request_id = request_id
        if payload.retry:
            # A retry must be traceable to a distinct request identity even if
            # a client reuses its visible request id after reconnecting.
            attempt_request_id = f"{request_id}:retry:{uuid4().hex}"
        try:
            if self.repository.get_active_attempt(window.window_id) is None:
                self._artifact_store.check_admission_budget(
                    self.resource_policy.max_artifact_directory_bytes, self._max_pending_windows)
            attempt = self.repository.create_attempt(
                window.window_id, attempt_request_id, max_active_attempts=self._max_pending_windows,
                max_project_attempts=self.resource_policy.max_project_attempts,
                max_recording_attempts=self.resource_policy.max_recording_attempts,
                producer_mode="on_demand", producer_owner_id=request_id, priority=100,
            )
        except ValueError as exc:
            # The single worker may have published the window between the
            # state read above and the attempt insert.  Treat that race as a
            # normal cache hit instead of surfacing a terminal-attempt error.
            current = self.repository.get_window(window.window_id)
            if current is None or current.state not in {"ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"}:
                raise exc
            result = self._read_artifact(current.result_artifact_id) if current.state == "ready" else None
            subscription_id = self.repository.create_subscription(
                current.window_id, request_id, project_id=payload.project_id,
            ).subscription_id
            return response(session.session_id, current, plan, request_id, result=result, subscription_id=subscription_id,
                                  scientific_cache_key=session_cache_key(session),
                                  scientific_configuration_json=session_scientific_configuration_json(session),
                                  playback_delivery=response_delivery)
        subscription_id = None if recovering is not None else self.repository.create_subscription(
            window.window_id, request_id, project_id=payload.project_id,
        ).subscription_id
        # A queued full producer owns this WindowKey. It is intentionally not
        # submitted to the on-demand executor; the full worker will publish or
        # release it through the shared coordinator.
        if attempt.state == "queued" and attempt.producer_mode != "full":
            with self._lock:
                if attempt.attempt_id not in self._pending:
                    future = self._executor.submit(
                        self._execute, window.window_id, session.session_id, capability,
                        typed_config, inputs, identity, plan, attempt.attempt_id,
                        attempt_request_id, frame_publisher, time.perf_counter())
                    self._pending[attempt.attempt_id] = future
                    self._logger.info(
                        "dynamic_window_queued algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s target_samples=%s:%s context_samples=%s:%s state=%s admission_ms=%.1f planning_ms=%.1f input_resolution_ms=%.1f window_planning_ms=%.1f",
                        identity.algorithm_id, identity.recording_id, attempt_request_id,
                        window.window_id, attempt.attempt_id, plan.start_sample, plan.end_sample,
                        plan.context_start_sample, plan.context_end_sample, window.state,
                        (time.perf_counter() - started) * 1000, planning_ms,
                        input_resolution_ms, window_planning_ms,
                    )
        return response(session.session_id, window, plan, request_id, subscription_id=subscription_id,
                              scientific_cache_key=session_cache_key(session),
                              scientific_configuration_json=session_scientific_configuration_json(session),
                              playback_delivery=response_delivery)

    def _recover_loop(self) -> None:
        while not self._stop.wait(1):
            try:
                self.repository.recover_expired_attempts()
                self.repository.recover_orphaned_full_producers()
                for window in self.repository.recoverable_windows(self._max_pending_windows):
                    if self._stop.is_set():
                        return
                    session = self.repository.get_session(window.session_id)
                    if session.algorithm_id not in {item.manifest.algorithm_id for item in self.capabilities.list()}:
                        continue
                    payload = DynamicWindowRequest(
                        recording_id=session.recording_id, algorithm_id=session.algorithm_id,
                        definition_id=session.definition_id, definition_version=session.definition_version,
                        config=session.configuration, cursor_sample=window.end_sample,
                        project_id=session.project_id, request_id=f"recovery-{uuid4().hex}",
                    )
                    try:
                        self._ensure(payload, recovering=window)
                    except DynamicWindowCapacityExceededError:
                        break
                    except (KeyError, ValueError) as exc:
                        self._logger.exception("dynamic_window_recovery_invalid window_id=%s", window.window_id)
                        current = self.repository.get_window(window.window_id)
                        if current.state == "queued":
                            self.repository.transition_window(window.window_id, "failed", quality="failed",
                                failure={"code": "WINDOW_RECOVERY_IDENTITY_INVALID", "message": str(exc)})
            except Exception:
                self._logger.exception("dynamic_window_recovery_failed")

    def get(self, window_id: str, *, project_id: str | None = None,
            include_projection_source: bool = False) -> dict[str, Any]:
        window = self.repository.get_window(window_id)
        if window is None:
            raise KeyError("dynamic window not found")
        session = self.repository.get_session(window.session_id)
        if session is None or session.project_id != project_id:
            raise KeyError("dynamic window not found in project scope")
        plan = plan_from_records(session, window)
        result = self._read_artifact(window.result_artifact_id,
            include_projection_source=include_projection_source) if window.state == "ready" else None
        return response(session.session_id, window, plan, result=result,
                              scientific_cache_key=session_cache_key(session),
                              scientific_configuration_json=session_scientific_configuration_json(session))

    def playback_events(self, delivery_id: str, *, after_sequence: int = 0,
                        limit: int = 256, project_id: str | None = None) -> dict[str, Any]:
        return self.playback_delivery.events(
            delivery_id, after_sequence=after_sequence, limit=limit, project_id=project_id,
        )

    def close_playback_delivery(self, delivery_id: str, *, project_id: str | None = None) -> None:
        self.playback_delivery.close(delivery_id, project_id=project_id)

    def subscribe(self, window_id: str, request_id: str, *, project_id: str | None = None) -> dict[str, Any]:
        return self.repository.create_subscription(window_id, request_id, project_id=project_id).__dict__

    def cancel_subscription(self, subscription_id: str, *, project_id: str | None = None) -> dict[str, Any]:
        subscription = self.repository.cancel_subscription(
            subscription_id, project_id=project_id, cancel_unobserved_work=True,
        )
        return subscription.__dict__

    def _execute(self, window_id: str, session_id: str, capability: Any, config: Any,
                 inputs: Any, identity: DynamicWindowIdentity, plan: DynamicWindowPlan,
                 attempt_id: str, request_id: str,
                 on_replayed_frame: Any | None = None, enqueued_at: float | None = None) -> None:
        self._logger.info(
            "dynamic_window_worker_started request_id=%s algorithm_id=%s recording_id=%s window_id=%s attempt_id=%s executor_queue_ms=%.3f",
            request_id, identity.algorithm_id, identity.recording_id, window_id, attempt_id,
            (time.perf_counter() - enqueued_at) * 1000 if enqueued_at is not None else 0.0,
        )
        try:
            with performance_trace("dynamic_window_worker_perf", request_id=request_id,
                    algorithm_id=identity.algorithm_id, recording_id=identity.recording_id,
                    window_id=window_id, attempt_id=attempt_id):
                execute_window_job(
                    self.repository, capability, config, inputs, identity, plan,
                    window_id, attempt_id, request_id, self._write_artifact,
                    self._remove_artifact_if_unreferenced, self._stop,
                    manifest_digest=self._artifact_store.manifest_digest,
                    resource_policy=self.resource_policy,
                    compute_coordinator=self.compute_coordinator,
                    on_replayed_frame=on_replayed_frame,
                )
        finally:
            with self._lock:
                self._pending.pop(attempt_id, None)

    def _write_artifact(self, window_id: str, result: DynamicWindowResult) -> str:
        window = self.repository.get_window(window_id)
        session = self.repository.get_session(window.session_id) if window is not None else None
        if session is not None:
            provenance_id = sha256_json({
                "scientific_cache_key": session_cache_key(session),
                "window_id": window_id,
                "producer_mode": "on_demand",
                "result_scope": "window",
            })
            result.provenance = {
                **result.provenance,
                "execution_contract_version": OFFLINE_EXECUTION_CONTRACT_VERSION,
                "execution_mode": "on_demand",
                "scientific_cache_key": session_cache_key(session),
                "producer_mode": "on_demand",
                "result_scope": "window",
                "served_from": "on_demand_computation",
                "provenance_id": provenance_id,
            }
        module = self.registry.get_current(result.identity.algorithm_id)
        fields = tuple(module.manifest.output_schema.get("projection_fields", ()))
        return self._artifact_store.write(window_id, result, projection_fields=fields)

    def _remove_artifact_if_unreferenced(self, artifact_id: str) -> None:
        self._artifact_store.remove_unreferenced(artifact_id)

    def _read_artifact(self, artifact_id: str | None, *,
                       include_projection_source: bool = False) -> dict[str, Any] | None:
        if not artifact_id:
            return None
        try:
            return self._artifact_store.read(
                artifact_id, manifest_sha256=self.repository.artifact_manifest_digest(artifact_id),
                include_projection_source=include_projection_source,
            )
        except (ValueError, OSError) as exc:
            raise DynamicWindowResultCorruptError(str(exc)) from exc

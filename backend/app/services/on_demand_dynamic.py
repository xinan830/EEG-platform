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
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowRequest,
    DynamicWindowResult,
)
from app.algorithm_runtime.errors import (
    DynamicWindowCapacityExceededError, DynamicWindowProjectForbiddenError,
    DynamicWindowCursorInvalidError, DynamicWindowResultCorruptError,
)
from app.algorithm_runtime.on_demand_registry import DynamicWindowCapabilityRegistry
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import sha256_json
from app.algorithm_runtime.offline_execution import (
    OFFLINE_EXECUTION_CONTRACT_VERSION,
    scientific_cache_key,
    scientific_configuration_sha256,
    scientific_context_snapshot,
)
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.persistence.repositories.dynamic_window import DynamicWindowRecord, DynamicWindowRepository
from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.services.recordings import RecordingService
from app.services.on_demand_worker import execute_window_job
from app.services.dynamic_window_artifacts import DynamicWindowArtifactStore
from app.services.dynamic_window_identity import DynamicWindowIdentityResolver
from app.services.projects import ProjectService
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator


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
        self.artifacts_dir = Path(artifacts_dir) / "dynamic-windows"
        self.artifacts_dir.mkdir(parents=True, exist_ok=True)
        # Keep admission and cache-hit timings in the application log stream.
        self._logger = logging.getLogger("brain_platform.dynamic_window")
        self._artifact_store = DynamicWindowArtifactStore(self.artifacts_dir, self.repository)
        self._artifact_store.cleanup()
        self._executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="dynamic-window")
        self._lock = threading.RLock()
        self._pending: dict[str, Any] = {}
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

    def ensure(self, payload: DynamicWindowRequest) -> dict[str, Any]:
        return self._ensure(payload)

    def _ensure(self, payload: DynamicWindowRequest, *, recovering: DynamicWindowRecord | None = None) -> dict[str, Any]:
        # Admission is short and serialized; scientific work runs outside this gate.
        with self._lock:
            if self._stop.is_set():
                raise DynamicWindowCapacityExceededError("动态分析服务正在关闭。")
            return self._ensure_locked(payload, recovering=recovering)

    def _ensure_locked(self, payload: DynamicWindowRequest, *, recovering: DynamicWindowRecord | None = None) -> dict[str, Any]:
        started = time.perf_counter()
        recording = self.recordings.require_recording(payload.recording_id)
        if payload.project_id is not None:
            if (self._projects.get(payload.project_id) is None or
                    recording.id not in self._projects.project_recording_ids(payload.project_id)):
                raise DynamicWindowProjectForbiddenError("记录不属于请求的项目。")
        if not recording.source_sha256 or not recording.sfreq or not recording.duration_s:
            raise ValueError("recording scientific identity or metadata is unavailable")
        capability = self.capabilities.get(payload.algorithm_id, payload.config.get("scientific_version") or self.registry.get(payload.algorithm_id).manifest.scientific_version)
        official = OfficialAlgorithmRunConfig.model_validate(self._official_payload(payload))
        module = self.registry.get(payload.algorithm_id, official.scientific_version)
        typed_config = module.config_model.model_validate(official.runtime_config())
        typed_config = self._runtime_validate(module, typed_config)
        context = _RecordingAlgorithmContext(self.recordings, recording)
        try:
            inputs = module.resolve_inputs(context, typed_config)
            max_cursor_sample = int(round(inputs.duration_s * inputs.sfreq_hz))
            if payload.cursor_sample > max_cursor_sample:
                raise DynamicWindowCursorInvalidError("cursor sample exceeds recording duration")
            resolved_identity = self._identity_resolver.resolve(module, payload)
            config = official.model_copy(update={"scientific_version": module.manifest.scientific_version}).model_dump(mode="json")
            if hasattr(self.recordings, "analysis_signal_provenance"):
                try:
                    signal_source = self.recordings.analysis_signal_provenance(recording)
                except ValueError as exc:
                    # Keep the window request auditable so the worker can return
                    # a structured unavailable result instead of publishing a
                    # fabricated ready payload for a damaged historical source.
                    signal_source = {
                        "signal_mode": "unverifiable",
                        "analysis_reference": "unverifiable_source_metadata",
                        "error_type": type(exc).__name__,
                        "error_message": str(exc),
                    }
            else:
                signal_source = {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}
            execution_snapshot = module.execution_snapshot(typed_config)
            channel_mapping = {
                "channels": module.requested_channels(typed_config),
                "signal_source": signal_source,
            }
            reference = {"mode": signal_source["analysis_reference"]}
            scientific_context = scientific_context_snapshot(
                sampling_rate_hz=inputs.sfreq_hz,
                signal_mapping=channel_mapping,
                reference=reference,
                filters=execution_snapshot.filters,
                quality_rules=execution_snapshot.quality_rules,
                window=execution_snapshot.window,
                window_policy_version=capability.window_policy_version,
            )
            scientific_config_sha256 = scientific_configuration_sha256(config, scientific_context)
            session_identity = DynamicSessionIdentity(
                project_scope=payload.project_id,
                recording_id=recording.id,
                source_sha256=recording.source_sha256,
                algorithm_id=module.manifest.algorithm_id,
                **resolved_identity,
                scientific_version=module.manifest.scientific_version,
                implementation_identity=module.manifest.implementation_identity,
                configuration_sha256=scientific_config_sha256,
                window_policy_version=capability.window_policy_version,
            )
            if recovering is not None:
                previous_session = self.repository.get_session(recovering.session_id)
                previous_identity = self._plan_from_records(previous_session, recovering).session_identity
                if previous_identity != session_identity:
                    raise ValueError("recovered source/configuration identity no longer matches its snapshot")
            session = self.repository.ensure_session(
                session_identity,
                configuration={**config, "_scientific_context": scientific_context},
            )
            plan = capability.plan_window(inputs, typed_config, payload.cursor_sample, session_identity)
        finally:
            context.close()

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
        request_id = payload.request_id or uuid4().hex
        if recovering is not None and window.window_id != recovering.window_id:
            raise ValueError("recovery resolved a different window generation")
        if window.state in {"ready", "insufficient_data", "rejected", "unavailable", "cancelled", "failed"}:
            result = self._read_artifact(window.result_artifact_id) if window.state == "ready" else None
            subscription_id = self.repository.create_subscription(
                window.window_id, request_id, project_id=payload.project_id,
            ).subscription_id
            self._logger.info(
                "dynamic_window_cache_hit window_id=%s state=%s elapsed_ms=%.1f",
                window.window_id, window.state, (time.perf_counter() - started) * 1000,
            )
            return self._response(session.session_id, window, plan, request_id, result=result, subscription_id=subscription_id,
                                  scientific_cache_key=self._session_cache_key(session))
        if plan.state != "planned":
            window = self.repository.transition_window(
                window.window_id, "insufficient_data", quality="insufficient_data",
                failure={"code": "WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window is not yet available"},
            )
            subscription_id = self.repository.create_subscription(
                window.window_id, request_id, project_id=payload.project_id,
            ).subscription_id
            return self._response(session.session_id, window, plan, request_id, subscription_id=subscription_id,
                                  scientific_cache_key=self._session_cache_key(session))
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
            return self._response(session.session_id, current, plan, request_id, result=result, subscription_id=subscription_id,
                                  scientific_cache_key=self._session_cache_key(session))
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
                        typed_config, inputs, identity, plan, attempt.attempt_id)
                    self._pending[attempt.attempt_id] = future
                    self._logger.info(
                        "dynamic_window_queued window_id=%s attempt_id=%s state=%s",
                        window.window_id, attempt.attempt_id, window.state,
                    )
        return self._response(session.session_id, window, plan, request_id, subscription_id=subscription_id,
                              scientific_cache_key=self._session_cache_key(session))

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

    def get(self, window_id: str, *, project_id: str | None = None) -> dict[str, Any]:
        window = self.repository.get_window(window_id)
        if window is None:
            raise KeyError("dynamic window not found")
        session = self.repository.get_session(window.session_id)
        if session is None or session.project_id != project_id:
            raise KeyError("dynamic window not found in project scope")
        plan = self._plan_from_records(session, window)
        result = self._read_artifact(window.result_artifact_id) if window.state == "ready" else None
        return self._response(session.session_id, window, plan, result=result,
                              scientific_cache_key=self._session_cache_key(session))

    def subscribe(self, window_id: str, request_id: str, *, project_id: str | None = None) -> dict[str, Any]:
        return self.repository.create_subscription(window_id, request_id, project_id=project_id).__dict__

    def cancel_subscription(self, subscription_id: str, *, project_id: str | None = None) -> dict[str, Any]:
        subscription = self.repository.cancel_subscription(
            subscription_id, project_id=project_id, cancel_unobserved_work=True,
        )
        return subscription.__dict__

    def _execute(self, window_id: str, session_id: str, capability: Any, config: Any,
                 inputs: Any, identity: DynamicWindowIdentity, plan: DynamicWindowPlan,
                 attempt_id: str) -> None:
        try:
            execute_window_job(
                self.repository, capability, config, inputs, identity, plan,
                window_id, attempt_id, self._write_artifact,
                self._remove_artifact_if_unreferenced, self._stop,
                manifest_digest=self._artifact_store.manifest_digest,
                resource_policy=self.resource_policy,
                compute_coordinator=self.compute_coordinator,
            )
        finally:
            with self._lock:
                self._pending.pop(attempt_id, None)

    @staticmethod
    def _runtime_validate(module: Any, config: Any) -> Any:
        if config.mode != "dynamic":
            raise ValueError("on-demand dynamic windows require dynamic mode")
        policy = module.manifest.dynamic_policy
        if config.window_s not in policy.window_options_s or config.step_s not in policy.refresh_step_options_s:
            raise ValueError("dynamic window or step is not supported by this algorithm")
        return config

    @staticmethod
    def _official_payload(payload: DynamicWindowRequest) -> dict[str, Any]:
        """Accept the two existing official-config spellings at this boundary."""
        raw = dict(payload.config)
        raw["algorithm_id"] = payload.algorithm_id
        if "time" not in raw and {"start_s", "end_s"} <= raw.keys():
            raw["time"] = {"start_s": raw.pop("start_s"), "end_s": raw.pop("end_s")}
        if "dynamic_window_s" not in raw and "window_s" in raw:
            raw["dynamic_window_s"] = raw.pop("window_s")
        if "refresh_step_s" not in raw and "step_s" in raw:
            raw["refresh_step_s"] = raw.pop("step_s")
        return raw

    def _write_artifact(self, window_id: str, result: DynamicWindowResult) -> str:
        window = self.repository.get_window(window_id)
        session = self.repository.get_session(window.session_id) if window is not None else None
        if session is not None:
            provenance_id = sha256_json({
                "scientific_cache_key": self._session_cache_key(session),
                "window_id": window_id,
                "producer_mode": "on_demand",
                "result_scope": "window",
            })
            result.provenance = {
                **result.provenance,
                "execution_contract_version": OFFLINE_EXECUTION_CONTRACT_VERSION,
                "execution_mode": "on_demand",
                "scientific_cache_key": self._session_cache_key(session),
                "producer_mode": "on_demand",
                "result_scope": "window",
                "served_from": "on_demand_computation",
                "provenance_id": provenance_id,
            }
        return self._artifact_store.write(window_id, result)

    def _remove_artifact_if_unreferenced(self, artifact_id: str) -> None:
        self._artifact_store.remove_unreferenced(artifact_id)

    def _read_artifact(self, artifact_id: str | None) -> dict[str, Any] | None:
        if not artifact_id:
            return None
        try:
            return self._artifact_store.read(
                artifact_id, manifest_sha256=self.repository.artifact_manifest_digest(artifact_id),
            )
        except (ValueError, OSError) as exc:
            raise DynamicWindowResultCorruptError(str(exc)) from exc

    @staticmethod
    def _session_cache_key(session: Any) -> str:
        configuration = dict(session.configuration)
        time_range = configuration.get("time") or {}
        return scientific_cache_key(
            source_sha256=session.source_sha256,
            definition_sha256=session.definition_sha256,
            config_sha256=session.configuration_sha256,
            implementation_build=session.implementation_build,
            actual_range={"start_s": float(time_range["start_s"]), "end_s": float(time_range["end_s"])},
        )

    @staticmethod
    def _plan_from_records(session: Any, window: DynamicWindowRecord) -> DynamicWindowPlan:
        identity = DynamicSessionIdentity(
            project_scope=session.project_id, recording_id=session.recording_id, source_sha256=session.source_sha256,
            algorithm_id=session.algorithm_id, definition_id=session.definition_id, definition_version=session.definition_version,
            definition_sha256=session.definition_sha256, implementation_build=session.implementation_build,
            scientific_version=session.scientific_version, implementation_identity=session.implementation_identity,
            configuration_sha256=session.configuration_sha256, window_policy_version=session.window_policy_version,
        )
        return DynamicWindowPlan(session_identity=identity, cursor_sample=window.end_sample,
                                 cursor_s=window.end_s,
                                 start_sample=window.start_sample, end_sample=window.end_sample,
                                 start_s=window.start_s, end_s=window.end_s,
                                 state="planned" if window.state not in {"insufficient_data", "unavailable"} else "insufficient_data",
                                 reason=window.failure.get("message") if window.failure else None)

    @staticmethod
    def _response(session_id: str, window: DynamicWindowRecord, plan: DynamicWindowPlan,
                  request_id: str | None = None, result: dict[str, Any] | None = None,
                  subscription_id: str | None = None,
                  scientific_cache_key: str | None = None) -> dict[str, Any]:
        identity = {
            **plan.session_identity.model_dump(mode="json"),
            "start_sample": window.start_sample,
            "end_sample": window.end_sample,
        }
        return {"session_id": session_id, "window_id": window.window_id,
                "request_id": request_id or f"window-status-{window.window_id}", "state": window.state,
                "subscription_id": subscription_id,
                "execution_mode": "on_demand",
                "scientific_cache_key": scientific_cache_key,
                "identity": {**identity, "generation": window.generation},
                "plan": plan.model_dump(mode="json"), "range": {"start_sample": window.start_sample, "end_sample": window.end_sample,
                "start_s": window.start_s, "end_s": window.end_s}, "quality": window.quality,
                "failure": window.failure, "result": result}

"""Lease-owned execution of one declared dynamic-window capability."""

from __future__ import annotations

import logging
import os
import threading
import time
from collections.abc import Callable
from datetime import datetime, timedelta, timezone
from typing import Any

from app.algorithm_runtime.on_demand import DynamicWindowIdentity, DynamicWindowPlan
from app.algorithm_runtime.on_demand_result_validation import validate_window_result
from app.algorithm_runtime.evidence import with_sample_coordinate
from app.algorithm_runtime.contracts import AlgorithmEvidence
from app.algorithm_runtime.errors import DynamicWindowResourceExceededError
from app.services.dynamic_window_resources import DynamicWindowResourcePolicy
from app.persistence.repositories.dynamic_window import DynamicWindowRepository


class WindowCancelled(RuntimeError):
    """The last consumer has released this execution."""


class WindowLeaseLost(RuntimeError):
    """This worker is no longer authorized to commit an outcome."""


class WindowExecutionControl:
    is_on_demand = True

    def __init__(self, check: Callable[[], None], prepared: Callable[[], None],
                 resource_policy: DynamicWindowResourcePolicy | None = None,
                 compute_coordinator: Any | None = None,
                 on_compute_slot_acquired: Callable[[float], None] | None = None) -> None:
        self.check = check
        self.prepared = prepared
        self.preparation_resource_policy = resource_policy
        self.compute_coordinator = compute_coordinator
        self.on_compute_slot_acquired = on_compute_slot_acquired
        self._compute_lease: Any | None = None

    def source_preparation_completed(self) -> None:
        self.check()
        self.prepared()

    def check_cancelled(self) -> None:
        self.check()

    def window_started(self) -> None:
        self.check()
        if self.compute_coordinator is not None:
            if self._compute_lease is not None:
                raise RuntimeError("a dynamic window is already active")
            self._compute_lease = self.compute_coordinator.acquire("interactive", self.check)
            if self.on_compute_slot_acquired is not None:
                self.on_compute_slot_acquired(self._compute_lease.waited_s)

    def window_completed(self) -> None:
        try:
            self.check()
        finally:
            self.close()

    def close(self) -> None:
        lease = self._compute_lease
        self._compute_lease = None
        if lease is not None:
            lease.release()


def execute_window_job(
    repository: DynamicWindowRepository, capability: Any, config: Any,
    inputs: Any, identity: DynamicWindowIdentity, plan: DynamicWindowPlan,
    window_id: str, attempt_id: str, request_id: str, write_artifact: Callable,
    remove_unreferenced: Callable[[str], None], stop: threading.Event,
    *, manifest_digest: Callable[[str], str], lease_seconds: float = 300,
    resource_policy: DynamicWindowResourcePolicy | None = None,
    compute_coordinator: Any | None = None,
    on_replayed_frame: Callable[[DynamicWindowIdentity, Any], None] | None = None,
) -> None:
    """Compute outside SQLite; all window mutations are transactionally fenced."""
    # The application file handler is scoped to the ``brain_platform``
    # namespace; keep worker lifecycle timings in that same stream.
    logger = logging.getLogger("brain_platform.dynamic_window")
    target_start_sample = plan.start_sample
    target_end_sample = plan.end_sample
    context_start_sample = plan.context_start_sample
    context_end_sample = plan.context_end_sample
    log_context = (
        identity.algorithm_id, identity.recording_id, request_id, window_id,
        attempt_id, target_start_sample, target_end_sample,
        context_start_sample, context_end_sample,
    )
    started = time.perf_counter()
    worker_id = f"dynamic-worker-{os.getpid()}"
    expiry = lambda: (datetime.now(timezone.utc) + timedelta(seconds=lease_seconds)).isoformat()
    claimed = repository.claim_attempt(window_id, worker_id, expiry())
    if claimed is None or claimed.attempt_id != attempt_id:
        return
    token = claimed.lease_token or ""
    heartbeat_stop = threading.Event()

    def check() -> None:
        current = repository.get_window(window_id)
        if current is None or current.state == "cancelled":
            raise WindowCancelled("dynamic window was cancelled")
        if stop.is_set() or not repository.is_attempt_active(attempt_id, token):
            raise WindowLeaseLost("dynamic window worker stopped or lease was lost")

    def heartbeat() -> None:
        while not heartbeat_stop.wait(max(0.05, lease_seconds / 3)):
            if stop.is_set():
                return
            try:
                repository.heartbeat_attempt(attempt_id, worker_id, expiry(), lease_token=token)
            except (KeyError, ValueError):
                return
            except Exception:
                logger.exception("dynamic_window_heartbeat_failed attempt_id=%s", attempt_id)
                return

    heartbeat_thread = threading.Thread(target=heartbeat, name="window-lease", daemon=True)
    artifact_id = None
    preparation_finished = False
    computing_started = None
    compute_slot_wait_ms = 0.0
    stage = "claim"

    def record_compute_slot_wait(waited_s: float) -> None:
        nonlocal compute_slot_wait_ms
        compute_slot_wait_ms = max(0.0, waited_s * 1000.0)

    def prepared() -> None:
        nonlocal preparation_finished, computing_started
        if not preparation_finished:
            repository.update_attempt_window(attempt_id, worker_id, token, "computing_window")
            preparation_finished = True
            computing_started = time.perf_counter()
            logger.info(
                "dynamic_window_source_prepared algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s target_samples=%s:%s context_samples=%s:%s worker_elapsed_ms=%.1f compute_slot_wait_ms=%.1f",
                *log_context, (time.perf_counter() - started) * 1000,
                compute_slot_wait_ms,
            )
    try:
        stage = "source_preparation"
        check()
        if resource_policy is not None:
            resource_policy.check_replay_budget(
                context_start_sample=plan.context_start_sample,
                context_end_sample=plan.context_end_sample,
                replay_window_count=plan.replay_window_count,
            )
        repository.update_attempt_window(attempt_id, worker_id, token, "preparing_source")
        heartbeat_thread.start()
        control = WindowExecutionControl(
            check, prepared, resource_policy, compute_coordinator,
            on_compute_slot_acquired=record_compute_slot_wait,
        )
        inputs.execution_context = control
        if hasattr(inputs.payload, "execution_context"):
            inputs.payload.execution_context = control
        control.window_started()
        stage = "algorithm_compute"
        if on_replayed_frame is not None and hasattr(capability, "execute_window_with_replayed_frames"):
            def publish_frame(frame_identity: DynamicWindowIdentity, frame_result: Any) -> None:
                check()
                frame_result.provenance = {
                    **frame_result.provenance,
                    "runtime_context": {
                        "reset_origin_sample": plan.session_identity.reset_origin_sample,
                        "context_identity": plan.context_identity,
                        "target_range_samples": {
                            "start_sample": frame_identity.start_sample,
                            "end_sample": frame_identity.end_sample,
                        },
                        "replay_window_count": frame_result.provenance.get("replayed_window_count"),
                    },
                }
                frame_result.evidence = AlgorithmEvidence.model_validate(with_sample_coordinate(
                    frame_result.evidence,
                    {"start_s": frame_identity.start_sample / inputs.sfreq_hz,
                     "end_s": frame_identity.end_sample / inputs.sfreq_hz},
                    inputs.sfreq_hz,
                )).model_dump(mode="json")
                validated = validate_window_result(frame_result, capability.manifest)
                on_replayed_frame(frame_identity, validated)

            result = capability.execute_window_with_replayed_frames(
                inputs, config, plan, identity, on_frame_result=publish_frame, check_cancelled=check,
            )
        else:
            result = capability.execute_window(inputs, config, plan, identity, check_cancelled=check)
        result.provenance = {
            **result.provenance,
            "runtime_context": {
                "reset_origin_sample": plan.session_identity.reset_origin_sample,
                "context_identity": plan.context_identity,
                "context_range_samples": {
                    "start_sample": plan.context_start_sample,
                    "end_sample": plan.context_end_sample,
                },
                "target_range_samples": {
                    "start_sample": plan.start_sample,
                    "end_sample": plan.end_sample,
                },
                "replay_window_count": plan.replay_window_count,
            },
        }
        result.evidence = AlgorithmEvidence.model_validate(with_sample_coordinate(
            result.evidence, {"start_s": plan.start_s, "end_s": plan.end_s}, inputs.sfreq_hz,
        )).model_dump(mode="json")
        stage = "result_validation"
        result = validate_window_result(result, capability.manifest)
        check()
        prepared()
        logger.info(
            "dynamic_window_computed algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s target_samples=%s:%s context_samples=%s:%s state=%s computing_ms=%.1f",
            *log_context, result.state, (time.perf_counter() - computing_started) * 1000,
        )
        if result.identity != identity:
            raise ValueError("dynamic window result identity does not match execution identity")
        if result.state == "ready":
            stage = "artifact_write"
            artifact_started = time.perf_counter()
            artifact_id = write_artifact(window_id, result)
            artifact_write_ms = (time.perf_counter() - artifact_started) * 1000
            check()
            stage = "result_publish"
            publish_started = time.perf_counter()
            repository.update_attempt_window(
                attempt_id, worker_id, token, "ready", quality=result.quality, artifact_id=artifact_id,
                manifest_sha256=manifest_digest(artifact_id),
            )
            logger.info(
                "dynamic_window_ready algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s target_samples=%s:%s context_samples=%s:%s worker_elapsed_ms=%.1f artifact_write_ms=%.1f database_publish_ms=%.1f",
                *log_context, (time.perf_counter() - started) * 1000,
                artifact_write_ms, (time.perf_counter() - publish_started) * 1000,
            )
        else:
            repository.update_attempt_window(
                attempt_id, worker_id, token, result.state, quality=result.quality,
                failure=result.failure or {"code": "WINDOW_NOT_READY", "message": result.state},
            )
    except WindowLeaseLost:
        logger.info(
            "dynamic_window_lease_lost algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s stage=%s target_samples=%s:%s context_samples=%s:%s",
            *log_context[:5], stage, *log_context[5:],
        )
    except Exception as exc:
        cancelled = isinstance(exc, WindowCancelled)
        if not cancelled:
            logger.exception(
                "dynamic_window_failed algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s stage=%s target_samples=%s:%s context_samples=%s:%s failure_code=%s",
                *log_context[:5], stage, *log_context[5:],
                exc.code if isinstance(exc, DynamicWindowResourceExceededError) else "WINDOW_EXECUTION_FAILED",
            )
        else:
            logger.info(
                "dynamic_window_cancelled algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s stage=%s target_samples=%s:%s context_samples=%s:%s",
                *log_context[:5], stage, *log_context[5:],
            )
        try:
            repository.update_attempt_window(
                attempt_id, worker_id, token, "cancelled" if cancelled else "failed",
                quality="cancelled" if cancelled else "failed",
                failure={"code": "WINDOW_CANCELLED" if cancelled else
                         exc.code if isinstance(exc, DynamicWindowResourceExceededError) else "WINDOW_EXECUTION_FAILED",
                         "message": str(exc), **({"details": exc.detail} if isinstance(exc, DynamicWindowResourceExceededError) else {})},
            )
        except (KeyError, ValueError):
            logger.info(
                "dynamic_window_stale_outcome_discarded algorithm_id=%s recording_id=%s request_id=%s window_id=%s attempt_id=%s stage=%s",
                *log_context[:5], stage,
            )
    finally:
        if 'control' in locals():
            control.close()
        heartbeat_stop.set()
        if heartbeat_thread.ident is not None:
            heartbeat_thread.join(timeout=5)
        if artifact_id is not None:
            remove_unreferenced(artifact_id)
        if hasattr(inputs.payload, "close"):
            inputs.payload.close()

"""Persistent queue and execution lifecycle for ordinary AnalysisRuns."""

from __future__ import annotations

from pathlib import Path
import logging
import time
from typing import Any
from uuid import uuid4

from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import build_cache_key, execution_environment, implementation_version, sha256_json
from app.scientific.quality import SpectralQualityGateError
from app.models.run import AnalysisRun, RunCreateRequest, RunStatus, StructuredRunError
from app.services.recordings import RecordingService
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository
from app.services.runs import RunService
from app.services.run_execution_context import RunCancelledError, RunExecutionContext
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.services.run_worker import RunWorker


logger = logging.getLogger("brain_platform.run")


class PersistentRunQueue:
    """Persists queue state; numerical execution remains in the backend service."""

    def __init__(
        self,
        recordings: RecordingService,
        database_path: Path = DATABASE_PATH,
        artifacts_dir: Path = ARTIFACTS_DIR,
    ):
        self.base = RunService(
            recordings,
            database_path,
            artifacts_dir,
        )
        self.recordings = recordings
        self.repository: RunRepository = self.base.repository
        self.repository.recover_interrupted()

    def enqueue(
        self,
        request: RunCreateRequest,
        *,
        parent_run_id: str | None = None,
        reuse_completed: bool = False,
    ) -> AnalysisRun:
        if request.preview:
            raise ValueError("definition previews use their dedicated endpoint")
        recording = self.recordings.require_recording(request.recording_id)
        if not recording.source_sha256:
            raise ValueError("recording source identity is unavailable")
        resolved = self.base._resolve_request(request, recording)
        config_sha256 = sha256_json(resolved["config"])
        build = implementation_version()
        cache_key = build_cache_key(
            source_sha256=recording.source_sha256, definition_sha256=resolved["definition_sha256"],
            config_sha256=config_sha256, implementation_build=build, actual_range=resolved["actual_range"],
        )
        if reuse_completed:
            cached = self.repository.find_completed_cache(cache_key, request.project_id)
            if cached is not None and self._cache_artifacts_are_valid(cached):
                return cached
        now = utc_now()
        run = AnalysisRun(
            run_id=uuid4().hex, recording_id=recording.id, analysis_type=request.analysis_type,
            status=RunStatus.QUEUED,
            definition_id=resolved["definition_id"],
            definition_version=resolved["definition_version"],
            scientific_version=resolved["scientific_version"], implementation_version=build, config=resolved["config"],
            config_sha256=config_sha256, cache_key=cache_key, requested_range=resolved["requested_range"],
            channel_mapping=resolved["channel_mapping"], reference=resolved["reference"], filters=resolved["filters"],
            window=resolved["window"], quality_rules=resolved["quality_rules"], environment=execution_environment(),
            project_id=request.project_id, batch_run_id=request.batch_run_id, idempotency_key=request.idempotency_key,
            parent_run_id=parent_run_id, created_at=now, updated_at=now,
        )
        run.progress_total = self._progress_total(run, recording)
        run.progress_stage = "queued"
        run.execution_lane = "batch" if request.batch_run_id or run.config.get("mode") == "dynamic" else "interactive"
        created = self.repository.create(run, deduplicate=True)
        logger.info(
            "run_queued run_id=%s recording_id=%s analysis_type=%s lane=%s progress_total=%s reused=%s",
            created.run_id, created.recording_id, created.analysis_type, created.execution_lane,
            created.progress_total, created.run_id != run.run_id,
        )
        return created

    def process_next(self, lane: str | None = None) -> AnalysisRun | None:
        run = self.repository.claim_next_queued(lane)
        if run is None:
            return None
        actual_range = run.actual_range
        started = time.perf_counter()
        logger.info(
            "run_started run_id=%s recording_id=%s analysis_type=%s lane=%s",
            run.run_id, run.recording_id, run.analysis_type, lane,
        )
        try:
            if self._is_cancelled(run.run_id):
                logger.info("run_cancelled_before_execution run_id=%s", run.run_id)
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED)
            recording = self.recordings.require_recording(run.recording_id)
            request = RunCreateRequest(recording_id=run.recording_id, analysis_type=run.analysis_type, config=run.config,
                                       definition_id=run.definition_id, definition_version=run.definition_version)
            resolved = self.base._resolve_request(request, recording)
            actual_range = resolved["actual_range"]
            total = self._progress_total(run, recording)
            self.repository.update_progress(run.run_id, 0, total, stage="analysis", message="准备分析")
            execution_context = RunExecutionContext(
                lambda: self._is_cancelled(run.run_id),
                lambda completed: self._record_progress(run, completed, total),
                total,
            )
            resolved["execution_context"] = execution_context
            cached = self.repository.find_completed_cache(run.cache_key, run.project_id)
            if cached is not None and cached.run_id != run.run_id:
                artifact_source_run_id = self.base.resolve_artifact_source_run_id(cached.run_id)
                execution_context.check_cancelled()
                self.repository.update_progress(run.run_id, total or 0, total, stage="completed", message="已复用相同配置的结果")
                logger.info("run_completed run_id=%s outcome=reused elapsed_ms=%.1f", run.run_id, _elapsed_ms(started))
                return self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=cached.actual_range,
                                                     result_summary=cached.result_summary, reused_from_run_id=artifact_source_run_id)
            result, arrays, unit = self.base.executor.execute(run.analysis_type, recording, resolved)
            execution_context.check_cancelled()
            if total is not None:
                self.repository.update_progress(run.run_id, total, total, stage="persisting", message="正在保存结果")
            if self._is_cancelled(run.run_id):
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED, actual_range=resolved["actual_range"])
            structured = result.get("structured") if isinstance(result, dict) else None
            window_ranges = structured.get("windows") if isinstance(structured, dict) else None
            artifact = self.base.artifacts.write_npz(
                run.run_id, run.analysis_type, arrays, unit,
                window_ranges=window_ranges if isinstance(window_ranges, list) else None,
            )
            final = self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=resolved["actual_range"],
                                                 result_summary={**result, "artifacts": [artifact.model_dump(mode="json")]})
            if final.status is RunStatus.CANCELLED:
                self.base.artifacts.discard_cancelled_write(artifact)
            logger.info("run_completed run_id=%s outcome=%s elapsed_ms=%.1f", run.run_id, final.status.value, _elapsed_ms(started))
            return final
        except RunCancelledError:
            logger.info("run_cancelled run_id=%s elapsed_ms=%.1f", run.run_id, _elapsed_ms(started))
            return self.repository.update_status(run.run_id, RunStatus.CANCELLED, actual_range=actual_range)
        except ValueError as exc:
            is_gate = isinstance(exc, SpectralQualityGateError)
            logger.warning(
                "run_failed run_id=%s recording_id=%s outcome=%s stage=analysis error_type=%s message=%s elapsed_ms=%.1f",
                run.run_id, run.recording_id, "gate_failed" if is_gate else "failed", type(exc).__name__, str(exc), _elapsed_ms(started),
            )
            return self.repository.update_status(
                run.run_id, RunStatus.GATE_FAILED if is_gate else RunStatus.FAILED, actual_range=actual_range,
                result_summary={"psd": None, "band_power": None, "relative_band_power": None, "quality": exc.quality} if is_gate else None,
                error=StructuredRunError(code="QUALITY_GATE_FAILED" if is_gate else "ANALYSIS_INPUT_INVALID", message=str(exc),
                                         stage="analysis", details={"quality": exc.quality} if is_gate else {}),
            )
        except Exception as exc:
            logger.exception(
                "run_failed run_id=%s recording_id=%s outcome=failed stage=analysis error_type=%s message=%s elapsed_ms=%.1f",
                run.run_id, run.recording_id, type(exc).__name__, str(exc), _elapsed_ms(started),
            )
            return self.repository.update_status(run.run_id, RunStatus.FAILED, actual_range=actual_range,
                error=StructuredRunError(code="ANALYSIS_EXECUTION_FAILED", message=str(exc) or f"分析执行抛出 {type(exc).__name__}", stage="analysis",
                                         details={"exception_type": type(exc).__name__, "exception_message": str(exc)}))

    def get(self, run_id: str) -> AnalysisRun:
        run = self.repository.get(run_id)
        if run is None:
            raise KeyError("run not found")
        return run

    def list(self, recording_id: str | None = None, limit: int = 100) -> list[AnalysisRun]:
        return self.repository.list(recording_id, limit)

    def cancel(self, run_id: str) -> AnalysisRun:
        return self.repository.request_cancel(run_id)

    def retry(self, run_id: str) -> AnalysisRun:
        previous = self.get(run_id)
        if previous.status not in {RunStatus.FAILED, RunStatus.GATE_FAILED, RunStatus.CANCELLED, RunStatus.INTERRUPTED}:
            raise ValueError("only terminal non-completed runs can be retried")
        return self.enqueue(RunCreateRequest(recording_id=previous.recording_id, analysis_type=previous.analysis_type,
                            config=previous.config, definition_id=previous.definition_id,
                            definition_version=previous.definition_version, project_id=previous.project_id,
                            batch_run_id=previous.batch_run_id), parent_run_id=previous.run_id)

    def list_artifacts(self, run_id: str):
        return self.repository.list_artifacts(self.base.resolve_artifact_source_run_id(run_id))

    def _is_cancelled(self, run_id: str) -> bool:
        current = self.get(run_id)
        return current.cancel_requested or current.status is RunStatus.CANCELLED

    def _record_progress(self, run: AnalysisRun, completed: int, total: int | None) -> None:
        message = f"已完成 {completed}/{total}" if total else "分析中"
        self.repository.update_progress(run.run_id, completed, total, stage="analysis", message=message)
        logger.info(
            "run_progress run_id=%s recording_id=%s analysis_type=%s completed=%s total=%s",
            run.run_id, run.recording_id, run.analysis_type, completed, total,
        )

    def _cache_artifacts_are_valid(self, run: AnalysisRun) -> bool:
        artifacts = self.repository.list_artifacts(self.base.resolve_artifact_source_run_id(run.run_id))
        if not artifacts:
            return False
        try:
            for artifact in artifacts:
                self.base.artifacts.verify(artifact)
        except (OSError, ValueError, RuntimeError):
            return False
        return True

    def _progress_total(self, run: AnalysisRun, recording: Any) -> int | None:
        if run.analysis_type != "official_algorithm":
            return 1
        config = OfficialAlgorithmRunConfig.model_validate(run.config)
        if config.mode != "dynamic":
            return 1
        module = self.base.algorithm_runtime_registry.get(config.algorithm_id, run.scientific_version)
        policy = module.manifest.dynamic_policy
        frames = build_dynamic_analysis_frames(config.time.start_s, config.time.end_s,
            duration_s=float(recording.duration_s), window_s=config.dynamic_window_s, step_s=config.refresh_step_s,
            minimum_window_s=policy.minimum_window_s, allow_warmup=policy.allow_warmup)
        return len(frames) or None


def _elapsed_ms(started: float) -> float:
    return (time.perf_counter() - started) * 1000

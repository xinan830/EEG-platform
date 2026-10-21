"""Persistent queue and execution lifecycle for ordinary AnalysisRuns."""

from __future__ import annotations

from pathlib import Path
import json
import logging
import math
import time
from typing import Any
from uuid import uuid4

from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import execution_environment, implementation_version, sha256_json
from app.algorithm_runtime.offline_execution import scientific_cache_key, scientific_configuration_sha256
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
from app.services.offline_window_reuse import OfflineWindowReuse
from app.algorithm_runtime.offline_execution import execution_provenance
from app.scientific.contracts.types import SampleRange
from app.services.scientific_compute_coordinator import ScientificComputeCoordinator


logger = logging.getLogger("brain_platform.run")


class FullCoverageError(ValueError):
    """Raised when a dynamic full Run did not produce a complete window set."""

    def __init__(self, details: dict[str, Any]):
        self.details = details
        super().__init__(details.get("message", "完整动态分析窗口覆盖不完整"))


class PersistentRunQueue:
    """Persists queue state; numerical execution remains in the backend service."""

    def __init__(
        self,
        recordings: RecordingService,
        database_path: Path = DATABASE_PATH,
        artifacts_dir: Path = ARTIFACTS_DIR,
        *,
        compute_coordinator: ScientificComputeCoordinator | None = None,
    ):
        self.base = RunService(
            recordings,
            database_path,
            artifacts_dir,
        )
        self.recordings = recordings
        self.repository: RunRepository = self.base.repository
        self._offline_window_reuse = OfflineWindowReuse(database_path, artifacts_dir)
        self.compute_coordinator = compute_coordinator or ScientificComputeCoordinator()
        self.repository.recover_interrupted()
        self._offline_window_reuse.repository.recover_orphaned_full_producers()

    def enqueue(
        self,
        request: RunCreateRequest,
        *,
        parent_run_id: str | None = None,
        reuse_completed: bool = False,
    ) -> AnalysisRun:
        if request.preview:
            raise ValueError("definition previews use their dedicated endpoint")
        if request.execution_mode != "full":
            raise ValueError("按需分析必须通过动态窗口接口执行，不能创建全量 Run")
        recording = self.recordings.require_recording(request.recording_id)
        if not recording.source_sha256:
            raise ValueError("recording source identity is unavailable")
        recording = self.base._recording_for_request(request, recording)
        resolved = self.base._resolve_request(request, recording)
        config_sha256 = sha256_json(resolved["config"])
        scientific_config_sha256 = scientific_configuration_sha256(
            resolved["config"], resolved["scientific_context"],
        )
        build = implementation_version()
        cache_key = scientific_cache_key(
            source_sha256=recording.source_sha256, definition_sha256=resolved["definition_sha256"],
            config_sha256=scientific_config_sha256, implementation_build=build, actual_range=resolved["actual_range"],
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
            execution_mode="full",
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
        execution_context: RunExecutionContext | None = None
        try:
            if self._is_cancelled(run.run_id):
                logger.info("run_cancelled_before_execution run_id=%s", run.run_id)
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED)
            recording = self.recordings.require_recording(run.recording_id)
            request = RunCreateRequest(recording_id=run.recording_id, analysis_type=run.analysis_type, config=run.config,
                                       definition_id=run.definition_id, definition_version=run.definition_version)
            recording = self.base._recording_for_request(request, recording)
            resolved = self.base._resolve_request(request, recording)
            actual_range = resolved["actual_range"]
            total = self._progress_total(run, recording)
            self.repository.update_progress(run.run_id, 0, total, stage="analysis", message="准备分析")
            execution_context = RunExecutionContext(
                lambda: self._is_cancelled(run.run_id),
                lambda completed: self._record_progress(run, completed, total),
                total,
                cached_window_provider=self._offline_window_reuse.provider(
                    run=run, resolved=resolved, registry=self.base.algorithm_runtime_registry,
                    source_sha256=recording.source_sha256 or ""),
                compute_coordinator=self.compute_coordinator,
            )
            resolved["execution_context"] = execution_context
            cached = self.repository.find_completed_cache(run.cache_key, run.project_id)
            if cached is not None and cached.run_id != run.run_id and self._cache_artifacts_are_valid(cached):
                artifact_source_run_id = self.base.resolve_artifact_source_run_id(cached.run_id)
                execution_context.check_cancelled()
                self.repository.update_progress(run.run_id, total or 0, total, stage="completed", message="已复用相同配置的结果")
                logger.info("run_completed run_id=%s outcome=reused elapsed_ms=%.1f", run.run_id, _elapsed_ms(started))
                return self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=cached.actual_range,
                                                     result_summary=cached.result_summary, reused_from_run_id=artifact_source_run_id)
            result, arrays, unit = self.base.executor.execute(run.analysis_type, recording, resolved)
            coverage, window_ranges = self._validate_full_coverage(
                run, recording, resolved, result, execution_context, total,
            )
            published_windows = self._offline_window_reuse.publish_full_windows(
                run=run, resolved=resolved, registry=self.base.algorithm_runtime_registry,
                source_sha256=recording.source_sha256 or "", result=result, arrays=arrays,
            )
            if published_windows:
                logger.info("full_windows_published run_id=%s count=%s", run.run_id, published_windows)
            if execution_context.reused_window_count:
                result["execution_provenance"] = execution_provenance(
                    "full", cache_key=run.cache_key,
                    reused_window_count=execution_context.reused_window_count,
                    cache_miss_reasons=execution_context.cache_miss_reasons,
                    served_from="on_demand_window_artifact" if execution_context.reused_window_count else "full_computation",
                    coverage=coverage,
                )
                logger.info("run_reused_on_demand_windows run_id=%s count=%s", run.run_id, execution_context.reused_window_count)
            elif execution_context.cache_miss_reasons:
                result["execution_provenance"] = execution_provenance(
                    "full", cache_key=run.cache_key,
                    cache_miss_reasons=execution_context.cache_miss_reasons,
                    served_from="full_computation",
                    coverage=coverage,
                )
            elif coverage is not None:
                result["execution_provenance"] = execution_provenance(
                    "full", cache_key=run.cache_key, served_from="full_computation", coverage=coverage,
                )
            elif run.analysis_type == "official_algorithm":
                result["execution_provenance"] = execution_provenance(
                    "full", cache_key=run.cache_key, served_from="full_computation",
                )
            execution_context.check_cancelled()
            if total is not None:
                self.repository.update_progress(run.run_id, total, total, stage="persisting", message="正在保存结果")
            if self._is_cancelled(run.run_id):
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED, actual_range=resolved["actual_range"])
            artifact = self.base.artifacts.write_npz(
                run.run_id, run.analysis_type, arrays, unit,
                window_ranges=window_ranges,
            )
            self.base.artifacts.verify(artifact)
            final = self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=resolved["actual_range"],
                                                 result_summary={**result, "artifacts": [artifact.model_dump(mode="json")]})
            if final.status is RunStatus.CANCELLED:
                self.base.artifacts.discard_cancelled_write(artifact)
            logger.info("run_completed run_id=%s outcome=%s elapsed_ms=%.1f", run.run_id, final.status.value, _elapsed_ms(started))
            return final
        except RunCancelledError:
            logger.info("run_cancelled run_id=%s elapsed_ms=%.1f", run.run_id, _elapsed_ms(started))
            return self.repository.update_status(run.run_id, RunStatus.CANCELLED, actual_range=actual_range)
        except FullCoverageError as exc:
            logger.warning(
                "run_failed run_id=%s recording_id=%s outcome=coverage_incomplete details=%s elapsed_ms=%.1f",
                run.run_id, run.recording_id, exc.details, _elapsed_ms(started),
            )
            return self.repository.update_status(
                run.run_id, RunStatus.FAILED, actual_range=actual_range,
                error=StructuredRunError(
                    code="FULL_COVERAGE_INCOMPLETE",
                    message=str(exc), stage="coverage", details=exc.details,
                ),
            )
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
        finally:
            # A failed or cancelled full Run must not leave queued producer
            # markers blocking later on-demand ownership takeover.
            self._offline_window_reuse.repository.release_producers(run.run_id)
            if execution_context is not None:
                execution_context.close()

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
            if run.analysis_type == "official_algorithm":
                config = OfficialAlgorithmRunConfig.model_validate(run.config)
                if config.mode == "dynamic":
                    if len(artifacts) != 1 or run.progress_total is None or run.progress_total <= 0:
                        return False
                    index_path = self.base.artifacts._window_index_path(artifacts[0])
                    if not index_path.is_file():
                        return False
                    index = json.loads(index_path.read_text(encoding="utf-8"))
                    windows = index.get("windows")
                    if index.get("format") != "result-window-index-v1" or not isinstance(windows, list):
                        return False
                    if len(windows) != run.progress_total or index.get("artifact_sha256") != artifacts[0].sha256:
                        return False
        except (OSError, ValueError, RuntimeError):
            return False
        except (TypeError, json.JSONDecodeError):
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

    def _validate_full_coverage(
        self,
        run: AnalysisRun,
        recording: Any,
        resolved: dict[str, Any],
        result: dict[str, Any],
        execution_context: RunExecutionContext,
        total: int | None,
    ) -> tuple[dict[str, Any] | None, list[dict[str, object]] | None]:
        """Gate dynamic completion on an explicit, indexable window contract."""
        if run.analysis_type != "official_algorithm":
            return None, None
        config = OfficialAlgorithmRunConfig.model_validate(resolved["config"])
        if config.mode != "dynamic":
            return None, None

        module = self.base.algorithm_runtime_registry.get(config.algorithm_id, run.scientific_version)
        policy = module.manifest.dynamic_policy
        frames = build_dynamic_analysis_frames(
            config.time.start_s, config.time.end_s,
            duration_s=float(recording.duration_s), window_s=config.dynamic_window_s,
            step_s=config.refresh_step_s, minimum_window_s=policy.minimum_window_s,
            allow_warmup=policy.allow_warmup,
        )
        expected = len(frames)
        if total != expected or execution_context.completed != expected:
            raise FullCoverageError({
                "message": "动态 Run 的执行进度没有覆盖全部计划窗口。",
                "planned_windows": expected,
                "progress_total": total,
                "progress_completed": execution_context.completed,
            })

        structured = result.get("structured") if isinstance(result, dict) else None
        metric = result.get("metric") if isinstance(result, dict) else None
        entries: list[dict[str, Any]] | None = None
        if isinstance(structured, dict) and isinstance(structured.get("windows"), list):
            entries = [item for item in structured["windows"] if isinstance(item, dict)]
            range_keys = ("start_s", "end_s")
            state_key = "state"
        elif isinstance(metric, dict) and isinstance(metric.get("series"), list):
            entries = [item for item in metric["series"] if isinstance(item, dict)]
            range_keys = ("window_start_s", "window_end_s")
            state_key = "analysis_state"
        if entries is None:
            raise FullCoverageError({
                "message": "动态 Run 未返回可验证的窗口序列。",
                "planned_windows": expected,
                "result_keys": sorted(result) if isinstance(result, dict) else [],
            })
        if len(entries) != expected:
            raise FullCoverageError({
                "message": "动态 Run 返回的窗口数量与计划不一致。",
                "planned_windows": expected,
                "returned_windows": len(entries),
            })

        allowed_states = {"Complete", "Partial", "Rejected", "Unavailable"}
        window_ranges: list[dict[str, object]] = []
        sfreq = float(recording.sfreq)
        for index, (frame, entry) in enumerate(zip(frames, entries)):
            state = entry.get(state_key)
            if state not in allowed_states:
                raise FullCoverageError({
                    "message": "动态 Run 包含未知窗口状态。",
                    "window_index": index, "state": state,
                })
            try:
                start_s = float(entry[range_keys[0]])
                end_s = float(entry[range_keys[1]])
            except (KeyError, TypeError, ValueError) as exc:
                raise FullCoverageError({
                    "message": "动态 Run 窗口缺少有效的实际范围。",
                    "window_index": index,
                }) from exc
            if not (math.isclose(start_s, frame.window_start_s, rel_tol=0.0, abs_tol=1e-9) and
                    math.isclose(end_s, frame.window_end_s, rel_tol=0.0, abs_tol=1e-9)):
                raise FullCoverageError({
                    "message": "动态 Run 窗口范围与计划不一致。",
                    "window_index": index,
                    "planned": {"start_s": frame.window_start_s, "end_s": frame.window_end_s},
                    "returned": {"start_s": start_s, "end_s": end_s},
                })
            if state in {"Rejected", "Unavailable"} and not entry.get("failure"):
                raise FullCoverageError({
                    "message": "拒绝或不可用窗口缺少结构化原因。",
                    "window_index": index, "state": state,
                })
            sample_range = SampleRange.from_seconds(start_s, end_s, sfreq)
            window_ranges.append({
                "start_sample": sample_range.start_sample,
                "end_sample": sample_range.end_sample,
                "state": state,
                "quality": entry.get("quality"),
            })
        return {
            "state": "complete",
            "planned_windows": expected,
            "terminal_windows": len(entries),
            "rejected_windows": sum(1 for entry in entries if entry.get(state_key) == "Rejected"),
            "unavailable_windows": sum(1 for entry in entries if entry.get(state_key) == "Unavailable"),
        }, window_ranges


def _elapsed_ms(started: float) -> float:
    return (time.perf_counter() - started) * 1000

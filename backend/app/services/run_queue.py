"""Single-worker SQLite queue for ordinary AnalysisRuns."""

from __future__ import annotations

import threading
from pathlib import Path
from typing import Any
from uuid import uuid4

from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import build_cache_key, execution_environment, implementation_version, sha256_json
from app.eeg_core.quality import SpectralQualityGateError
from app.models.run import AnalysisRun, RunCreateRequest, RunStatus, StructuredRunError
from app.services.recordings import RecordingService
from app.services.run_repository import RunRepository, utc_now
from app.services.runs import RunService


class PersistentRunQueue:
    """Persists queue state; numerical execution remains in the backend service."""

    def __init__(self, recordings: RecordingService, database_path: Path = DATABASE_PATH, artifacts_dir: Path = ARTIFACTS_DIR):
        self.base = RunService(recordings, database_path, artifacts_dir)
        self.recordings = recordings
        self.repository: RunRepository = self.base.repository
        self.repository.recover_interrupted()

    def enqueue(self, request: RunCreateRequest, *, parent_run_id: str | None = None) -> AnalysisRun:
        if request.preview:
            raise ValueError("definition previews use their dedicated endpoint")
        if request.idempotency_key:
            existing = self.repository.find_idempotency_key(request.idempotency_key)
            if existing is not None:
                return existing
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
        self.repository.create(run)
        return run

    def process_next(self) -> AnalysisRun | None:
        run = self.repository.claim_next_queued()
        if run is None:
            return None
        actual_range = run.actual_range
        try:
            if self._is_cancelled(run.run_id):
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED)
            recording = self.recordings.require_recording(run.recording_id)
            request = RunCreateRequest(recording_id=run.recording_id, analysis_type=run.analysis_type, config=run.config,
                                       definition_id=run.definition_id, definition_version=run.definition_version)
            resolved = self.base._resolve_request(request, recording)
            actual_range = resolved["actual_range"]
            cached = self.repository.find_completed_cache(run.cache_key)
            if cached is not None and cached.run_id != run.run_id:
                return self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=cached.actual_range,
                                                     result_summary=cached.result_summary, reused_from_run_id=cached.run_id)
            result, arrays, unit = self.base.executor.execute(run.analysis_type, recording, resolved)
            if self._is_cancelled(run.run_id):
                return self.repository.update_status(run.run_id, RunStatus.CANCELLED, actual_range=resolved["actual_range"])
            artifact = self.base.artifacts.write_npz(run.run_id, run.analysis_type, arrays, unit)
            return self.repository.update_status(run.run_id, RunStatus.COMPLETED, actual_range=resolved["actual_range"],
                                                 result_summary={**result, "artifacts": [artifact.model_dump(mode="json")]})
        except ValueError as exc:
            is_gate = isinstance(exc, SpectralQualityGateError)
            return self.repository.update_status(
                run.run_id, RunStatus.GATE_FAILED if is_gate else RunStatus.FAILED, actual_range=actual_range,
                result_summary={"psd": None, "band_power": None, "relative_band_power": None, "quality": exc.quality} if is_gate else None,
                error=StructuredRunError(code="QUALITY_GATE_FAILED" if is_gate else "ANALYSIS_INPUT_INVALID", message=str(exc),
                                         stage="analysis", details={"quality": exc.quality} if is_gate else {}),
            )
        except Exception as exc:
            return self.repository.update_status(run.run_id, RunStatus.FAILED, actual_range=actual_range,
                error=StructuredRunError(code="ANALYSIS_EXECUTION_FAILED", message=str(exc), stage="analysis",
                                         details={"exception_type": type(exc).__name__}))

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
        run = self.get(run_id)
        return self.repository.list_artifacts(run.reused_from_run_id or run.run_id)

    def create_definition_preview(self, *args: Any, **kwargs: Any):
        return self.base.create_definition_preview(*args, **kwargs)

    def _is_cancelled(self, run_id: str) -> bool:
        current = self.get(run_id)
        return current.cancel_requested or current.status is RunStatus.CANCELLED


class RunWorker:
    """One daemon worker; queue ownership never leaks into Viewer state."""

    def __init__(self, queue: PersistentRunQueue):
        self.queue = queue
        self._wake = threading.Event()
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._loop, name="analysis-run-worker", daemon=True)
        self._thread.start()

    def wake(self) -> None:
        self._wake.set()

    def stop(self) -> None:
        self._stop.set()
        self._wake.set()
        if self._thread:
            self._thread.join(timeout=2.0)

    def _loop(self) -> None:
        while not self._stop.is_set():
            while not self._stop.is_set() and self.queue.process_next() is not None:
                pass
            self._wake.wait(0.25)
            self._wake.clear()

"""Project-scoped batch expansion over the persistent local run queue."""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.core.provenance import sha256_json
from app.models.batch_run import BatchRun, BatchRunCreateRequest, BatchRunItem
from app.models.run import RunCreateRequest, RunStatus
from app.persistence import connect_database, migrate_database
from app.services.projects import ProjectService
from app.services.run_queue import PersistentRunQueue
from app.persistence.clock import utc_now


class BatchProjectMembershipError(ValueError):
    pass


class BatchRunService:
    def __init__(self, projects: ProjectService, queue: PersistentRunQueue, database_path: Path = DATABASE_PATH):
        self.projects, self.queue, self.database_path = projects, queue, Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path, foreign_keys=True)

    def create(self, request: BatchRunCreateRequest) -> BatchRun:
        allowed = self.projects.project_recording_ids(request.project_id)
        requested = list(dict.fromkeys(request.recording_ids))
        if set(requested) - allowed:
            raise BatchProjectMembershipError("recording is not associated with this project")
        if request.idempotency_key:
            existing = self._get_by_key(request.idempotency_key)
            if existing:
                return existing
        now = utc_now()
        batch = BatchRun(batch_run_id=uuid4().hex, project_id=request.project_id, analysis_type=request.analysis_type,
                         definition_id=request.definition_id, definition_version=request.definition_version,
                         config=request.config, config_sha256=sha256_json(request.config),
                         idempotency_key=request.idempotency_key, status="queued", created_at=now, updated_at=now)
        with self._connect() as connection:
            connection.execute("INSERT INTO batch_runs VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (batch.batch_run_id, batch.project_id, batch.analysis_type, batch.definition_id, batch.definition_version,
                 json.dumps(batch.config, sort_keys=True), batch.config_sha256, batch.idempotency_key, batch.status, now, now))
        for recording_id in requested:
            preflight = self._preflight(recording_id, request.config)
            if preflight:
                self._save_item(BatchRunItem(batch_run_id=batch.batch_run_id, recording_id=recording_id,
                                              outcome=preflight, error_code=preflight.upper()))
                continue
            run = self.queue.enqueue(RunCreateRequest(recording_id=recording_id, analysis_type=request.analysis_type,
                config=request.config, definition_id=request.definition_id, definition_version=request.definition_version,
                project_id=request.project_id, batch_run_id=batch.batch_run_id,
                idempotency_key=f"batch:{batch.batch_run_id}:{recording_id}"))
            self._save_item(BatchRunItem(batch_run_id=batch.batch_run_id, recording_id=recording_id, run_id=run.run_id, outcome="queued"))
        return self.get(batch.batch_run_id)

    def get(self, batch_run_id: str) -> BatchRun:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM batch_runs WHERE batch_run_id = ?", (batch_run_id,)).fetchone()
        if row is None:
            raise KeyError("batch run not found")
        batch = self._batch(row)
        return batch.model_copy(update={"status": self._status(batch_run_id)})

    def items(self, batch_run_id: str) -> list[BatchRunItem]:
        self.get(batch_run_id)
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM batch_run_items WHERE batch_run_id = ? ORDER BY recording_id", (batch_run_id,)).fetchall()
        return [self._item(row) for row in rows]

    def cancel(self, batch_run_id: str) -> BatchRun:
        for item in self.items(batch_run_id):
            if item.run_id:
                try:
                    self.queue.cancel(item.run_id)
                except ValueError:
                    pass
        return self.get(batch_run_id)

    def retry(self, batch_run_id: str) -> BatchRun:
        for item in self.items(batch_run_id):
            if not item.run_id or item.outcome in {"completed", "queued", "running"}:
                continue
            try:
                retry = self.queue.retry(item.run_id)
            except ValueError:
                continue
            self._save_item(BatchRunItem(batch_run_id=batch_run_id, recording_id=item.recording_id, run_id=retry.run_id, outcome="queued"))
        return self.get(batch_run_id)

    def _preflight(self, recording_id: str, config: dict[str, object]) -> str | None:
        recording = self.queue.recordings.require_recording(recording_id)
        requested = [str(name).casefold() for name in config.get("channels", [])] if isinstance(config.get("channels", []), list) else []
        available = {name.casefold() for name in recording.channels}
        if requested and not set(requested).issubset(available):
            return "missing_channel"
        time = config.get("time", {})
        if isinstance(time, dict) and "start_s" in time and "end_s" in time:
            required = float(time["end_s"]) - float(time["start_s"])
            if required > float(recording.duration_s or 0.0):
                return "insufficient_duration"
        return None

    def _status(self, batch_run_id: str) -> str:
        outcomes = [item.outcome for item in self.items_raw(batch_run_id)]
        if not outcomes:
            return "completed"
        if any(item in {"queued", "running"} for item in outcomes):
            return "running"
        if all(item == "cancelled" for item in outcomes):
            return "cancelled"
        return "completed"

    def items_raw(self, batch_run_id: str) -> list[BatchRunItem]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM batch_run_items WHERE batch_run_id = ? ORDER BY recording_id", (batch_run_id,)).fetchall()
        return [self._item(row) for row in rows]

    def _save_item(self, item: BatchRunItem) -> None:
        with self._connect() as connection:
            connection.execute("INSERT OR REPLACE INTO batch_run_items VALUES (?, ?, ?, ?, ?)", tuple(item.model_dump().values()))

    def _item(self, row: sqlite3.Row) -> BatchRunItem:
        outcome, code = str(row["outcome"]), row["error_code"]
        if row["run_id"]:
            run = self.queue.get(str(row["run_id"]))
            outcome = run.status.value
            code = run.error.code if run.error else None
        return BatchRunItem(batch_run_id=row["batch_run_id"], recording_id=row["recording_id"], run_id=row["run_id"], outcome=outcome, error_code=code)

    def _get_by_key(self, key: str) -> BatchRun | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM batch_runs WHERE idempotency_key = ?", (key,)).fetchone()
        return self._batch(row) if row else None

    @staticmethod
    def _batch(row: sqlite3.Row) -> BatchRun:
        return BatchRun(batch_run_id=row["batch_run_id"], project_id=row["project_id"], analysis_type=row["analysis_type"],
            definition_id=row["definition_id"], definition_version=row["definition_version"], config=json.loads(row["config_json"]),
            config_sha256=row["config_sha256"], idempotency_key=row["idempotency_key"], status=row["status"],
            created_at=row["created_at"], updated_at=row["updated_at"])

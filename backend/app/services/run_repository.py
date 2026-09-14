"""SQLite persistence for analysis runs, artifacts, and validation evidence."""

from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from app.core.config import DATABASE_PATH
from app.models.run import AnalysisRun, RunArtifact, RunStatus, StructuredRunError, ValidationRun
from app.persistence import migrate_database


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True)


def _loads(value: str | None, default: Any = None) -> Any:
    return json.loads(value) if value is not None else default


class RunRepository:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path, timeout=5.0)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        return connection

    def create(self, run: AnalysisRun) -> None:
        values = run.model_dump(mode="json")
        with self._connect() as connection:
            connection.execute(
                """INSERT INTO analysis_runs (
                    run_id, recording_id, analysis_type, status, definition_id, definition_version,
                    scientific_version, implementation_version, config_json, config_sha256, cache_key,
                    requested_range_json, actual_range_json, channel_mapping_json, reference_json,
                    filter_json, window_json, quality_rules_json, environment_json, result_summary_json,
                    error_json, is_preview, reused_from_run_id, created_at, updated_at, started_at, completed_at
                    , project_id, batch_run_id, idempotency_key, parent_run_id, cancel_requested
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    run.run_id, run.recording_id, run.analysis_type, run.status.value,
                    run.definition_id, run.definition_version, run.scientific_version,
                    run.implementation_version, _json(values["config"]), run.config_sha256,
                    run.cache_key, _json(values["requested_range"]),
                    _json(values["actual_range"]) if values["actual_range"] is not None else None,
                    _json(values["channel_mapping"]), _json(values["reference"]),
                    _json(values["filters"]), _json(values["window"]),
                    _json(values["quality_rules"]), _json(values["environment"]),
                    _json(values["result_summary"]) if values["result_summary"] is not None else None,
                    _json(values["error"]) if values["error"] is not None else None,
                    int(run.is_preview), run.reused_from_run_id, run.created_at, run.updated_at,
                    run.started_at, run.completed_at,
                    run.project_id, run.batch_run_id, run.idempotency_key, run.parent_run_id, int(run.cancel_requested),
                ),
            )

    def get(self, run_id: str) -> AnalysisRun | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM analysis_runs WHERE run_id = ?", (run_id,)).fetchone()
        return self._run(row) if row else None

    def list(self, recording_id: str | None = None, limit: int = 100) -> list[AnalysisRun]:
        bounded = max(1, min(int(limit), 1000))
        query = "SELECT * FROM analysis_runs"
        values: list[Any] = []
        if recording_id:
            query += " WHERE recording_id = ?"
            values.append(recording_id)
        query += " ORDER BY created_at DESC LIMIT ?"
        values.append(bounded)
        with self._connect() as connection:
            rows = connection.execute(query, values).fetchall()
        return [self._run(row) for row in rows]

    def find_completed_cache(self, cache_key: str) -> AnalysisRun | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM analysis_runs WHERE cache_key = ? AND status = 'completed' ORDER BY completed_at DESC LIMIT 1",
                (cache_key,),
            ).fetchone()
        return self._run(row) if row else None

    def find_idempotency_key(self, idempotency_key: str) -> AnalysisRun | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM analysis_runs WHERE idempotency_key = ?", (idempotency_key,)).fetchone()
        return self._run(row) if row else None

    def claim_next_queued(self) -> AnalysisRun | None:
        """Atomically claim one job; only the local single worker calls this."""
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT run_id FROM analysis_runs WHERE status = 'queued' ORDER BY created_at LIMIT 1").fetchone()
            if row is None:
                connection.execute("COMMIT")
                return None
            now = utc_now()
            updated = connection.execute(
                "UPDATE analysis_runs SET status = 'running', updated_at = ?, started_at = ? WHERE run_id = ? AND status = 'queued'",
                (now, now, row["run_id"]),
            ).rowcount
            connection.execute("COMMIT")
        return self.get(str(row["run_id"])) if updated else None

    def request_cancel(self, run_id: str) -> AnalysisRun:
        run = self.get(run_id)
        if run is None:
            raise KeyError("run not found")
        if run.status is RunStatus.QUEUED:
            return self.update_status(run_id, RunStatus.CANCELLED)
        if run.status is not RunStatus.RUNNING:
            raise ValueError(f"run in {run.status.value} state cannot be cancelled")
        with self._connect() as connection:
            connection.execute("UPDATE analysis_runs SET cancel_requested = 1, updated_at = ? WHERE run_id = ?", (utc_now(), run_id))
        updated = self.get(run_id)
        assert updated is not None
        return updated

    def recover_interrupted(self) -> int:
        """Record a startup interruption then make jobs eligible for re-claim."""
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            now = utc_now()
            count = connection.execute("UPDATE analysis_runs SET status = 'interrupted', updated_at = ? WHERE status = 'running'", (now,)).rowcount
            connection.execute("UPDATE analysis_runs SET status = 'queued', updated_at = ? WHERE status = 'interrupted'", (now,))
            connection.execute("COMMIT")
        return int(count)

    def update_status(
        self,
        run_id: str,
        status: RunStatus,
        *,
        actual_range: dict[str, float] | None = None,
        result_summary: dict[str, Any] | None = None,
        error: StructuredRunError | None = None,
        reused_from_run_id: str | None = None,
    ) -> AnalysisRun:
        current = self.get(run_id)
        if current is None:
            raise KeyError("run not found")
        allowed = {
            RunStatus.QUEUED: {RunStatus.RUNNING, RunStatus.CANCELLED, RunStatus.FAILED},
            RunStatus.RUNNING: {RunStatus.COMPLETED, RunStatus.GATE_FAILED, RunStatus.FAILED, RunStatus.CANCELLED, RunStatus.INTERRUPTED},
            RunStatus.INTERRUPTED: {RunStatus.QUEUED, RunStatus.CANCELLED},
        }
        if status not in allowed.get(current.status, set()):
            raise ValueError(f"invalid run transition: {current.status.value} -> {status.value}")
        now = utc_now()
        started_at = now if status is RunStatus.RUNNING else current.started_at
        completed_at = now if status not in {RunStatus.QUEUED, RunStatus.RUNNING} else None
        with self._connect() as connection:
            connection.execute(
                """UPDATE analysis_runs SET status = ?, updated_at = ?, started_at = ?, completed_at = ?,
                   actual_range_json = COALESCE(?, actual_range_json),
                   result_summary_json = COALESCE(?, result_summary_json),
                   error_json = ?, reused_from_run_id = COALESCE(?, reused_from_run_id)
                   WHERE run_id = ?""",
                (
                    status.value, now, started_at, completed_at,
                    _json(actual_range) if actual_range is not None else None,
                    _json(result_summary) if result_summary is not None else None,
                    _json(error.model_dump(mode="json")) if error else None,
                    reused_from_run_id, run_id,
                ),
            )
        updated = self.get(run_id)
        assert updated is not None
        return updated

    def add_artifact(self, artifact: RunArtifact) -> None:
        with self._connect() as connection:
            connection.execute(
                """INSERT INTO run_artifacts
                   (artifact_id, run_id, kind, relative_path, media_type, byte_size, sha256, unit, shape_json, created_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    artifact.artifact_id, artifact.run_id, artifact.kind, artifact.relative_path,
                    artifact.media_type, artifact.byte_size, artifact.sha256, artifact.unit,
                    _json(artifact.shape), artifact.created_at,
                ),
            )

    def list_artifacts(self, run_id: str) -> list[RunArtifact]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT * FROM run_artifacts WHERE run_id = ? ORDER BY created_at", (run_id,)
            ).fetchall()
        return [RunArtifact(
            artifact_id=row["artifact_id"], run_id=row["run_id"], kind=row["kind"],
            relative_path=row["relative_path"], media_type=row["media_type"],
            byte_size=row["byte_size"], sha256=row["sha256"], unit=row["unit"],
            shape=_loads(row["shape_json"], {}), created_at=row["created_at"],
        ) for row in rows]

    @staticmethod
    def _run(row: sqlite3.Row) -> AnalysisRun:
        return AnalysisRun(
            run_id=row["run_id"], recording_id=row["recording_id"],
            analysis_type=row["analysis_type"], status=RunStatus(row["status"]),
            definition_id=row["definition_id"], definition_version=row["definition_version"],
            scientific_version=row["scientific_version"], implementation_version=row["implementation_version"],
            config=_loads(row["config_json"], {}), config_sha256=row["config_sha256"], cache_key=row["cache_key"],
            requested_range=_loads(row["requested_range_json"], {}), actual_range=_loads(row["actual_range_json"]),
            channel_mapping=_loads(row["channel_mapping_json"], {}), reference=_loads(row["reference_json"], {}),
            filters=_loads(row["filter_json"], {}), window=_loads(row["window_json"], {}),
            quality_rules=_loads(row["quality_rules_json"], {}), environment=_loads(row["environment_json"], {}),
            result_summary=_loads(row["result_summary_json"]),
            error=StructuredRunError(**_loads(row["error_json"])) if row["error_json"] else None,
            is_preview=bool(row["is_preview"]), reused_from_run_id=row["reused_from_run_id"],
            project_id=row["project_id"], batch_run_id=row["batch_run_id"], idempotency_key=row["idempotency_key"],
            parent_run_id=row["parent_run_id"], cancel_requested=bool(row["cancel_requested"]),
            created_at=row["created_at"], updated_at=row["updated_at"], started_at=row["started_at"],
            completed_at=row["completed_at"],
        )


class ValidationRepository:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path, timeout=5.0)
        connection.row_factory = sqlite3.Row
        return connection

    def create(self, validation: ValidationRun) -> None:
        values = validation.model_dump(mode="json")
        with self._connect() as connection:
            connection.execute(
                """INSERT INTO validation_runs (
                   validation_id, kind, status, subject_run_id, algorithm_id, algorithm_version,
                   dataset_identity_json, config_sha256, tolerances_json, expected_summary_json,
                   actual_summary_json, max_absolute_error, max_relative_error, point_count,
                   passed_point_count, pass_rate, passed, environment_json, error_json, created_at, completed_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    validation.validation_id, validation.kind, validation.status, validation.subject_run_id,
                    validation.algorithm_id, validation.algorithm_version, _json(values["dataset_identity"]),
                    validation.config_sha256, _json(values["tolerances"]),
                    _json(values["expected_summary"]) if values["expected_summary"] is not None else None,
                    _json(values["actual_summary"]) if values["actual_summary"] is not None else None,
                    validation.max_absolute_error, validation.max_relative_error, validation.point_count,
                    validation.passed_point_count, validation.pass_rate,
                    int(validation.passed) if validation.passed is not None else None,
                    _json(values["environment"]),
                    _json(values["error"]) if values["error"] is not None else None,
                    validation.created_at, validation.completed_at,
                ),
            )

    def get(self, validation_id: str) -> ValidationRun | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT * FROM validation_runs WHERE validation_id = ?", (validation_id,)
            ).fetchone()
        return self._validation(row) if row else None

    def list(self, limit: int = 100) -> list[ValidationRun]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT * FROM validation_runs ORDER BY created_at DESC LIMIT ?",
                (max(1, min(int(limit), 1000)),),
            ).fetchall()
        return [self._validation(row) for row in rows]

    @staticmethod
    def _validation(row: sqlite3.Row) -> ValidationRun:
        return ValidationRun(
            validation_id=row["validation_id"], kind=row["kind"], status=row["status"],
            subject_run_id=row["subject_run_id"], algorithm_id=row["algorithm_id"],
            algorithm_version=row["algorithm_version"], dataset_identity=_loads(row["dataset_identity_json"], {}),
            config_sha256=row["config_sha256"], tolerances=_loads(row["tolerances_json"], {}),
            expected_summary=_loads(row["expected_summary_json"]), actual_summary=_loads(row["actual_summary_json"]),
            max_absolute_error=row["max_absolute_error"], max_relative_error=row["max_relative_error"],
            point_count=row["point_count"], passed_point_count=row["passed_point_count"], pass_rate=row["pass_rate"],
            passed=bool(row["passed"]) if row["passed"] is not None else None,
            environment=_loads(row["environment_json"], {}),
            error=StructuredRunError(**_loads(row["error_json"])) if row["error_json"] else None,
            created_at=row["created_at"], completed_at=row["completed_at"],
        )

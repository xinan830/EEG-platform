"""Persistence boundary for immutable on-demand dynamic analysis windows.

This repository deliberately owns storage and lifecycle checks only. Queueing,
algorithm execution, and artifact publication remain service responsibilities.
"""

from __future__ import annotations

import json
import math
import sqlite3
from pathlib import Path
from typing import Any
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.persistence import connect_database, migrate_database
from app.persistence.clock import utc_now
from app.algorithm_runtime.on_demand import DynamicWindowIdentity, OnDemandWindowState
from app.algorithm_runtime.on_demand_lifecycle import TERMINAL_WINDOW_STATES, require_transition
from .dynamic_attempt import DynamicAttemptRepositoryMixin
from .dynamic_records import DynamicSessionRecord, DynamicWindowRecord, DynamicSubscriptionRecord


def _json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True)


def _loads(value: str | None) -> dict[str, Any] | None:
    return json.loads(value) if value is not None else None


class DynamicWindowRepository(DynamicAttemptRepositoryMixin):
    """Transactional storage for sessions, windows, and subscriptions."""

    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path, foreign_keys=True)

    def ensure_session(self, identity: DynamicWindowIdentity, *, configuration: dict[str, Any],
                       status: str = "queued") -> DynamicSessionRecord:
        """Return one session for the exact scientific configuration identity."""
        now = utc_now()
        values = (
            uuid4().hex, identity.project_scope, identity.recording_id, identity.source_sha256,
            identity.algorithm_id, identity.definition_id, identity.definition_version,
            identity.scientific_version, identity.implementation_identity,
            identity.configuration_sha256, _json(configuration), identity.window_policy_version,
            status, now, now,
            identity.definition_sha256, identity.implementation_build,
        )
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            connection.execute(
                """INSERT INTO dynamic_analysis_sessions(
                    session_id, project_id, recording_id, source_sha256, algorithm_id, definition_id,
                    definition_version, scientific_version, implementation_identity, configuration_sha256,
                    configuration_json, window_policy_version, status, created_at, updated_at,
                    definition_sha256, implementation_build
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT DO NOTHING""",
                values,
            )
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_sessions
                   WHERE recording_id = ? AND source_sha256 = ? AND algorithm_id = ?
                     AND definition_id = ? AND definition_version = ? AND scientific_version = ?
                     AND implementation_identity = ? AND configuration_sha256 = ?
                     AND window_policy_version = ? AND project_id IS ?
                     AND definition_sha256 = ? AND implementation_build = ?""",
                (identity.recording_id, identity.source_sha256, identity.algorithm_id,
                 identity.definition_id, identity.definition_version, identity.scientific_version,
                 identity.implementation_identity, identity.configuration_sha256,
                 identity.window_policy_version, identity.project_scope,
                 identity.definition_sha256, identity.implementation_build),
            ).fetchone()
            if row is not None and _json(json.loads(row["configuration_json"])) != _json(configuration):
                raise ValueError("dynamic session identity conflicts with its configuration snapshot")
        if row is None:
            raise RuntimeError("dynamic session was not persisted")
        return self._session(row)

    def get_session(self, session_id: str) -> DynamicSessionRecord | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM dynamic_analysis_sessions WHERE session_id = ?", (session_id,)).fetchone()
        return self._session(row) if row else None

    def find_session(self, identity: DynamicWindowIdentity) -> DynamicSessionRecord | None:
        """Find a session only when every persisted scientific identity field matches."""
        with self._connect() as connection:
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_sessions
                   WHERE recording_id = ? AND source_sha256 = ? AND algorithm_id = ?
                     AND definition_id = ? AND definition_version = ? AND scientific_version = ?
                     AND implementation_identity = ? AND configuration_sha256 = ?
                     AND window_policy_version = ? AND project_id IS ?
                     AND definition_sha256 = ? AND implementation_build = ?""",
                (identity.recording_id, identity.source_sha256, identity.algorithm_id,
                 identity.definition_id, identity.definition_version, identity.scientific_version,
                 identity.implementation_identity, identity.configuration_sha256,
                 identity.window_policy_version, identity.project_scope,
                 identity.definition_sha256, identity.implementation_build),
            ).fetchone()
        return self._session(row) if row else None

    def latest_windows(self, session_id: str) -> list[DynamicWindowRecord]:
        """Return one newest generation per recording-relative range."""
        with self._connect() as connection:
            rows = connection.execute(
                """SELECT w.* FROM dynamic_analysis_windows w
                   WHERE w.session_id = ? AND NOT EXISTS (
                       SELECT 1 FROM dynamic_analysis_windows newer
                       WHERE newer.session_id = w.session_id
                         AND newer.start_sample = w.start_sample
                         AND newer.end_sample = w.end_sample
                         AND newer.generation > w.generation
                   ) ORDER BY w.start_sample, w.end_sample""", (session_id,),
            ).fetchall()
        return [self._window(row) for row in rows]

    def ensure_window(self, session_id: str, *, start_sample: int, end_sample: int,
                      start_s: float, end_s: float, state: OnDemandWindowState = "queued",
                      quality: str = "pending") -> DynamicWindowRecord:
        """Ensure a pending window without replacing any existing result."""
        if type(start_sample) is not int or type(end_sample) is not int:
            raise ValueError("dynamic window sample indices must be integers")
        if (start_sample < 0 or end_sample <= start_sample or
                not math.isfinite(start_s) or not math.isfinite(end_s) or
                start_s < 0 or end_s <= start_s):
            raise ValueError("dynamic window must use a positive half-open range")
        if state not in {"queued", "preparing_source"}:
            raise ValueError("new dynamic window must be queued or preparing_source")
        now = utc_now()
        window_id = uuid4().hex
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            connection.execute(
                """INSERT INTO dynamic_analysis_windows(
                    window_id, session_id, generation, start_sample, end_sample, start_s, end_s,
                    state, quality, created_at, updated_at
                ) VALUES (?, ?, 0, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(session_id, start_sample, end_sample, generation) DO NOTHING""",
                (window_id, session_id, start_sample, end_sample, start_s, end_s, state, quality, now, now),
            )
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_windows
                   WHERE session_id = ? AND start_sample = ? AND end_sample = ?
                   ORDER BY generation DESC LIMIT 1""",
                (session_id, start_sample, end_sample),
            ).fetchone()
            if row is not None and (row["start_s"], row["end_s"]) != (start_s, end_s):
                raise ValueError("dynamic window sample range conflicts with its time coordinates")
        if row is None:
            raise RuntimeError("dynamic window was not persisted")
        return self._window(row)

    def retry_window(self, session_id: str, *, start_sample: int, end_sample: int,
                     start_s: float, end_s: float, retry_of_window_id: str) -> DynamicWindowRecord:
        """Create the next immutable generation after a cancelled/failed window."""
        if type(start_sample) is not int or type(end_sample) is not int:
            raise ValueError("dynamic window sample indices must be integers")
        if (start_sample < 0 or end_sample <= start_sample or
                not math.isfinite(start_s) or not math.isfinite(end_s) or
                start_s < 0 or end_s <= start_s):
            raise ValueError("dynamic window must use a positive half-open range")
        now = utc_now()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            original = connection.execute(
                """SELECT * FROM dynamic_analysis_windows WHERE window_id = ?
                   AND session_id = ? AND start_sample = ? AND end_sample = ?""",
                (retry_of_window_id, session_id, start_sample, end_sample),
            ).fetchone()
            if original is None:
                raise ValueError("retry window identity does not match the requested window")
            if original["state"] not in {"cancelled", "failed"}:
                raise ValueError("only cancelled or failed dynamic windows can be retried")
            if (original["start_s"], original["end_s"]) != (start_s, end_s):
                raise ValueError("retry window time coordinates conflict")
            latest = connection.execute(
                """SELECT * FROM dynamic_analysis_windows
                   WHERE session_id = ? AND start_sample = ? AND end_sample = ?
                   ORDER BY generation DESC LIMIT 1""",
                (session_id, start_sample, end_sample),
            ).fetchone()
            if latest is None:
                raise KeyError("dynamic window not found")
            # Repeated/concurrent retries of the same original generation
            # resolve to its already-created successor, even if it has failed.
            if int(latest["generation"]) > int(original["generation"]):
                successor = connection.execute(
                    """SELECT * FROM dynamic_analysis_windows WHERE session_id = ?
                       AND start_sample = ? AND end_sample = ? AND generation = ?""",
                    (session_id, start_sample, end_sample, int(original["generation"]) + 1),
                ).fetchone()
                return self._window(successor)
            if latest["state"] not in {"cancelled", "failed"}:
                raise ValueError("only cancelled or failed dynamic windows can be retried")
            generation = int(latest["generation"]) + 1
            window_id = uuid4().hex
            connection.execute(
                """INSERT INTO dynamic_analysis_windows(
                    window_id, session_id, generation, start_sample, end_sample,
                    start_s, end_s, state, quality, created_at, updated_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, 'queued', 'pending', ?, ?)""",
                (window_id, session_id, generation, start_sample, end_sample, start_s, end_s, now, now),
            )
            row = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
        assert row is not None
        return self._window(row)

    def latest_window(self, session_id: str, *, start_sample: int, end_sample: int) -> DynamicWindowRecord | None:
        with self._connect() as connection:
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_windows
                   WHERE session_id = ? AND start_sample = ? AND end_sample = ?
                   ORDER BY generation DESC LIMIT 1""",
                (session_id, start_sample, end_sample),
            ).fetchone()
        return self._window(row) if row else None

    def get_window(self, window_id: str) -> DynamicWindowRecord | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
        return self._window(row) if row else None

    def transition_window(self, window_id: str, target: OnDemandWindowState, *, quality: str | None = None,
                          failure: dict[str, Any] | None = None) -> DynamicWindowRecord:
        """Advance non-ready states; READY is reserved for the atomic publisher."""
        if target == "ready":
            raise ValueError("ready dynamic window requires atomic artifact publication")
        if target in {"rejected", "unavailable", "failed"} and failure is None:
            raise ValueError(f"{target} dynamic window requires a structured failure")
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
            if row is None:
                raise KeyError("dynamic window not found")
            require_transition(str(row["state"]), target)
            now = utc_now()
            terminal = target in TERMINAL_WINDOW_STATES
            connection.execute(
                """UPDATE dynamic_analysis_windows SET state = ?, quality = COALESCE(?, quality),
                   failure_json = COALESCE(?, failure_json),
                   updated_at = ?, completed_at = CASE WHEN ? THEN ? ELSE completed_at END
                   WHERE window_id = ?""",
                (target, quality, _json(failure) if failure is not None else None,
                 now, int(terminal), now if terminal else None, window_id),
            )
            updated = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
        assert updated is not None
        return self._window(updated)

    def publish_ready_window(self, window_id: str, artifact_id: str, *, quality: str = "clean") -> DynamicWindowRecord:
        """Commit a verified artifact reference and READY state together."""
        if not artifact_id:
            raise ValueError("ready dynamic window requires an artifact identity")
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
            if row is None:
                raise KeyError("dynamic window not found")
            require_transition(str(row["state"]), "ready")
            now = utc_now()
            connection.execute(
                """UPDATE dynamic_analysis_windows SET state = 'ready', quality = ?,
                   result_artifact_id = ?, updated_at = ?, completed_at = ?
                   WHERE window_id = ?""",
                (quality, artifact_id, now, now, window_id),
            )
            updated = connection.execute("SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
        assert updated is not None
        return self._window(updated)

    def create_subscription(self, window_id: str, request_id: str, *, project_id: str | None = None) -> DynamicSubscriptionRecord:
        """Create a subscriber inside its window's project scope, not another."""
        if not request_id:
            raise ValueError("dynamic subscription requires a request identity")
        now = utc_now()
        subscription_id = uuid4().hex
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute(
                """SELECT s.project_id FROM dynamic_analysis_windows w
                   JOIN dynamic_analysis_sessions s ON s.session_id = w.session_id
                   WHERE w.window_id = ?""", (window_id,),
            ).fetchone()
            if row is None or row["project_id"] != project_id:
                raise KeyError("dynamic window not found in project scope")
            connection.execute(
                """INSERT INTO dynamic_analysis_subscriptions(
                    subscription_id, window_id, request_id, project_id, state, created_at, updated_at
                ) VALUES (?, ?, ?, ?, 'active', ?, ?)""",
                (subscription_id, window_id, request_id, project_id, now, now),
            )
            row = connection.execute("SELECT * FROM dynamic_analysis_subscriptions WHERE subscription_id = ?", (subscription_id,)).fetchone()
        assert row is not None
        return self._subscription(row)

    def cancel_subscription(self, subscription_id: str, *, project_id: str | None = None,
                            cancel_unobserved_work: bool = False) -> DynamicSubscriptionRecord:
        """Cancel one scoped subscriber without changing shared work state."""
        now = utc_now()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT * FROM dynamic_analysis_subscriptions WHERE subscription_id = ? AND project_id IS ?", (subscription_id, project_id)).fetchone()
            if row is None:
                raise KeyError("dynamic subscription not found")
            if row["state"] == "active":
                connection.execute("UPDATE dynamic_analysis_subscriptions SET state = 'cancelled', updated_at = ?, cancelled_at = ? WHERE subscription_id = ?", (now, now, subscription_id))
            if cancel_unobserved_work:
                active = connection.execute(
                    "SELECT COUNT(*) FROM dynamic_analysis_subscriptions WHERE window_id = ? AND state = 'active'",
                    (row["window_id"],),
                ).fetchone()[0]
                if active == 0:
                    connection.execute(
                        """UPDATE dynamic_analysis_windows SET state = 'cancelled', quality = 'cancelled',
                           failure_json = ?, updated_at = ?, completed_at = ? WHERE window_id = ?
                           AND state IN ('queued', 'preparing_source', 'computing_window')""",
                        (_json({"code": "WINDOW_NO_ACTIVE_SUBSCRIBERS", "message": "窗口已无活动订阅者"}), now, now, row["window_id"]),
                    )
                    connection.execute(
                        """UPDATE dynamic_analysis_attempts SET state = 'cancelled', updated_at = ?
                           WHERE window_id = ? AND state = 'queued'""", (now, row["window_id"]),
                    )
            updated = connection.execute("SELECT * FROM dynamic_analysis_subscriptions WHERE subscription_id = ?", (subscription_id,)).fetchone()
        assert updated is not None
        return self._subscription(updated)

    def active_subscription_count(self, window_id: str) -> int:
        with self._connect() as connection:
            row = connection.execute("SELECT COUNT(*) AS count FROM dynamic_analysis_subscriptions WHERE window_id = ? AND state = 'active'", (window_id,)).fetchone()
        return int(row["count"])

    def published_artifact_ids(self) -> set[str]:
        """Return only artifact identities referenced by a persisted READY window."""
        with self._connect() as connection:
            rows = connection.execute(
                """SELECT result_artifact_id FROM dynamic_analysis_windows
                   WHERE state = 'ready' AND result_artifact_id IS NOT NULL"""
            ).fetchall()
        return {str(row["result_artifact_id"]) for row in rows}

    def artifact_manifest_digest(self, artifact_id: str) -> str | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT result_manifest_sha256 FROM dynamic_analysis_windows WHERE result_artifact_id = ? AND state = 'ready'",
                (artifact_id,),
            ).fetchone()
        return row[0] if row else None

    def has_running_attempts(self) -> bool:
        with self._connect() as connection:
            return connection.execute("SELECT 1 FROM dynamic_analysis_attempts WHERE state = 'running' LIMIT 1").fetchone() is not None

    def recoverable_windows(self, limit: int) -> list[DynamicWindowRecord]:
        """Bounded pending work whose persisted consumers still need a result."""
        with self._connect() as connection:
            rows = connection.execute(
                """SELECT w.* FROM dynamic_analysis_windows w
                   WHERE w.state IN ('queued', 'preparing_source', 'computing_window')
                   AND EXISTS (SELECT 1 FROM dynamic_analysis_subscriptions s
                       WHERE s.window_id = w.window_id AND s.state = 'active')
                   AND NOT EXISTS (SELECT 1 FROM dynamic_analysis_attempts a
                       WHERE a.window_id = w.window_id AND a.state = 'running')
                   ORDER BY w.updated_at, w.window_id LIMIT ?""", (limit,),
            ).fetchall()
        return [self._window(row) for row in rows]

    @staticmethod
    def _session(row: sqlite3.Row) -> DynamicSessionRecord:
        return DynamicSessionRecord(
            session_id=row["session_id"], project_id=row["project_id"], recording_id=row["recording_id"],
            source_sha256=row["source_sha256"], algorithm_id=row["algorithm_id"], definition_id=row["definition_id"],
            definition_version=row["definition_version"], scientific_version=row["scientific_version"],
            definition_sha256=row["definition_sha256"], implementation_build=row["implementation_build"],
            implementation_identity=row["implementation_identity"], configuration_sha256=row["configuration_sha256"],
            configuration=json.loads(row["configuration_json"]), window_policy_version=row["window_policy_version"],
            status=row["status"], created_at=row["created_at"], updated_at=row["updated_at"],
        )

    @staticmethod
    def _window(row: sqlite3.Row) -> DynamicWindowRecord:
        return DynamicWindowRecord(
            window_id=row["window_id"], session_id=row["session_id"], generation=int(row["generation"]),
            start_sample=row["start_sample"],
            end_sample=row["end_sample"], start_s=row["start_s"], end_s=row["end_s"], state=row["state"],
            quality=row["quality"], failure=_loads(row["failure_json"]), result_artifact_id=row["result_artifact_id"],
            created_at=row["created_at"], updated_at=row["updated_at"], completed_at=row["completed_at"],
        )

    @staticmethod
    def _subscription(row: sqlite3.Row) -> DynamicSubscriptionRecord:
        return DynamicSubscriptionRecord(
            subscription_id=row["subscription_id"], window_id=row["window_id"], request_id=row["request_id"],
            project_id=row["project_id"], state=row["state"], created_at=row["created_at"],
            updated_at=row["updated_at"], cancelled_at=row["cancelled_at"],
        )

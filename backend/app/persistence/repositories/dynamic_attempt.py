"""Fenced lease persistence for on-demand dynamic-window attempts."""

from __future__ import annotations

import json
import sqlite3
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any
from uuid import uuid4

from app.algorithm_runtime.on_demand_lifecycle import TERMINAL_WINDOW_STATES, require_transition
from app.algorithm_runtime.errors import DynamicWindowCapacityExceededError
from app.persistence.clock import utc_now


def _json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True)


def _loads(value: str | None) -> dict[str, Any] | None:
    return json.loads(value) if value is not None else None


def _utc_timestamp(value: str, field: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{field} must be an ISO-8601 timestamp") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise ValueError(f"{field} must include a UTC offset")
    return parsed.astimezone(timezone.utc)


@dataclass(frozen=True)
class DynamicAttemptRecord:
    attempt_id: str
    window_id: str
    request_id: str
    state: str
    worker_id: str | None
    lease_token: str | None
    lease_expires_at: str | None
    heartbeat_at: str | None
    error: dict[str, Any] | None
    created_at: str
    updated_at: str
    producer_mode: str
    producer_owner_id: str | None
    priority: int


class DynamicAttemptRepositoryMixin:
    """Methods mixed into the window repository to preserve one DB boundary."""

    def create_attempt(self, window_id: str, request_id: str, *, max_active_attempts: int | None = None,
                       max_project_attempts: int | None = None,
                       max_recording_attempts: int | None = None,
                       producer_mode: str = "on_demand", producer_owner_id: str | None = None,
                       priority: int = 0) -> DynamicAttemptRecord:
        if not request_id:
            raise ValueError("dynamic attempt requires a request identity")
        if producer_mode not in {"full", "on_demand"}:
            raise ValueError("dynamic attempt producer_mode must be 'full' or 'on_demand'")
        if type(priority) is not int:
            raise ValueError("dynamic attempt priority must be an integer")
        now = utc_now()
        attempt_id = uuid4().hex
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            window = connection.execute("SELECT state FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
            if window is None:
                raise KeyError("dynamic window not found")
            if window["state"] in TERMINAL_WINDOW_STATES:
                raise ValueError("terminal dynamic window cannot receive a new attempt")
            existing = connection.execute(
                "SELECT * FROM dynamic_analysis_attempts WHERE window_id = ? AND state IN ('queued', 'running')",
                (window_id,),
            ).fetchone()
            if existing is not None:
                # A queued full producer can be promoted by an interactive
                # consumer, but a running producer remains fenced and cannot
                # be stolen mid-computation.
                if existing["state"] == "queued" and priority > int(existing["priority"] or 0):
                    connection.execute(
                        "UPDATE dynamic_analysis_attempts SET priority = ?, updated_at = ? WHERE attempt_id = ? AND state = 'queued'",
                        (priority, now, existing["attempt_id"]),
                    )
                    existing = connection.execute(
                        "SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?",
                        (existing["attempt_id"],),
                    ).fetchone()
                return self._attempt(existing)
            scope = connection.execute(
                """SELECT s.project_id, s.recording_id FROM dynamic_analysis_windows w
                   JOIN dynamic_analysis_sessions s ON s.session_id=w.session_id WHERE w.window_id=?""",
                (window_id,),
            ).fetchone()
            for column, limit in (("project_id", max_project_attempts), ("recording_id", max_recording_attempts)):
                if limit is None:
                    continue
                active = connection.execute(
                    f"""SELECT COUNT(*) FROM dynamic_analysis_attempts a
                        JOIN dynamic_analysis_windows w ON w.window_id=a.window_id
                        JOIN dynamic_analysis_sessions s ON s.session_id=w.session_id
                        WHERE a.state IN ('queued','running') AND s.{column} IS ?""", (scope[column],),
                ).fetchone()[0]
                if active >= limit:
                    raise DynamicWindowCapacityExceededError("动态分析域内任务已达到容量上限。",
                        detail={"scope": column, "limit": limit})
            if max_active_attempts is not None:
                active = connection.execute(
                    "SELECT COUNT(*) FROM dynamic_analysis_attempts WHERE state IN ('queued', 'running')",
                ).fetchone()[0]
                if active >= max_active_attempts:
                    raise DynamicWindowCapacityExceededError(
                        "动态分析队列已达到容量上限，请稍后重试。",
                        detail={"max_pending_windows": max_active_attempts},
                    )
            try:
                connection.execute(
                    """INSERT INTO dynamic_analysis_attempts(
                        attempt_id, window_id, request_id, state, created_at, updated_at,
                        producer_mode, producer_owner_id, priority
                    ) VALUES (?, ?, ?, 'queued', ?, ?, ?, ?, ?)""",
                    (attempt_id, window_id, request_id, now, now, producer_mode, producer_owner_id, priority),
                )
            except sqlite3.IntegrityError as exc:
                existing = connection.execute(
                    """SELECT * FROM dynamic_analysis_attempts
                       WHERE window_id = ? AND state IN ('queued', 'running')
                       ORDER BY created_at, attempt_id LIMIT 1""", (window_id,),
                ).fetchone()
                if existing is None:
                    raise exc
                return self._attempt(existing)
            row = connection.execute("SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
        assert row is not None
        return self._attempt(row)

    def get_attempt(self, attempt_id: str) -> DynamicAttemptRecord | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
        return self._attempt(row) if row else None

    def get_active_attempt(self, window_id: str) -> DynamicAttemptRecord | None:
        with self._connect() as connection:
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_attempts
                   WHERE window_id = ? AND state IN ('queued', 'running')
                   ORDER BY priority DESC, created_at, attempt_id LIMIT 1""", (window_id,),
            ).fetchone()
        return self._attempt(row) if row else None

    def transfer_queued_producer(
        self,
        window_id: str,
        *,
        expected_owner_id: str,
        new_owner_id: str,
        producer_mode: str,
        priority: int,
    ) -> DynamicAttemptRecord:
        """Transfer a queued producer without touching a running lease."""
        if not expected_owner_id or not new_owner_id:
            raise ValueError("producer ownership transfer requires both owners")
        if producer_mode not in {"full", "on_demand"}:
            raise ValueError("dynamic attempt producer_mode must be 'full' or 'on_demand'")
        if type(priority) is not int:
            raise ValueError("dynamic attempt priority must be an integer")
        now = utc_now()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_attempts
                   WHERE window_id = ? AND state = 'queued'
                   ORDER BY priority DESC, created_at, attempt_id LIMIT 1""",
                (window_id,),
            ).fetchone()
            if row is None:
                raise KeyError("queued dynamic producer not found")
            if row["producer_owner_id"] != expected_owner_id:
                raise ValueError("queued producer owner does not match transfer expectation")
            connection.execute(
                """UPDATE dynamic_analysis_attempts
                   SET producer_mode = ?, producer_owner_id = ?, priority = ?, updated_at = ?
                   WHERE attempt_id = ? AND state = 'queued'""",
                (producer_mode, new_owner_id, priority, now, row["attempt_id"]),
            )
            updated = connection.execute(
                "SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (row["attempt_id"],)
            ).fetchone()
        assert updated is not None
        return self._attempt(updated)

    def wait_for_window_terminal(
        self,
        window_id: str,
        *,
        timeout_s: float = 30.0,
        poll_s: float = 0.05,
        check_cancelled: Any | None = None,
    ):
        """Wait briefly for an existing dynamic producer before a full Run computes.

        This is a coordination aid, not a second queue.  The database remains
        the source of truth, so a process restart or lease expiry still lets
        the full Run fall back to its validated computation path.
        """
        if timeout_s < 0 or poll_s <= 0:
            raise ValueError("window wait values must be positive")
        deadline = time.monotonic() + timeout_s
        while True:
            if check_cancelled is not None:
                check_cancelled()
            window = self.get_window(window_id)
            if window is None:
                return None
            if window.state in TERMINAL_WINDOW_STATES:
                return window
            active = self.get_active_attempt(window_id)
            if active is None or time.monotonic() >= deadline:
                return window
            time.sleep(min(poll_s, max(0.0, deadline - time.monotonic())))

    def is_attempt_active(self, attempt_id: str, lease_token: str) -> bool:
        now = utc_now()
        with self._connect() as connection:
            row = connection.execute(
                """SELECT state, lease_expires_at, lease_token
                   FROM dynamic_analysis_attempts WHERE attempt_id = ?""", (attempt_id,),
            ).fetchone()
        if row is None or row["state"] != "running" or row["lease_token"] != lease_token:
            return False
        if not row["lease_expires_at"]:
            return False
        return _utc_timestamp(row["lease_expires_at"], "lease_expires_at") > _utc_timestamp(now, "current time")

    def claim_attempt(self, window_id: str, worker_id: str, lease_expires_at: str) -> DynamicAttemptRecord | None:
        if not worker_id:
            raise ValueError("attempt claim requires a worker identity")
        expiry = _utc_timestamp(lease_expires_at, "lease_expires_at")
        now_text = utc_now()
        now = _utc_timestamp(now_text, "current time")
        if expiry <= now:
            raise ValueError("lease_expires_at must be in the future")
        expiry_text = expiry.isoformat()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            window = connection.execute("SELECT state FROM dynamic_analysis_windows WHERE window_id = ?", (window_id,)).fetchone()
            if window is None:
                raise KeyError("dynamic window not found")
            if window["state"] in TERMINAL_WINDOW_STATES:
                return None
            self._expire_running(connection, window_id, now)
            row = connection.execute(
                """SELECT * FROM dynamic_analysis_attempts
                   WHERE window_id = ? AND state = 'queued'
                   ORDER BY priority DESC, created_at, attempt_id LIMIT 1""", (window_id,),
            ).fetchone()
            if row is None:
                return None
            token = uuid4().hex
            updated = connection.execute(
                """UPDATE dynamic_analysis_attempts
                   SET state = 'running', worker_id = ?, lease_token = ?, lease_expires_at = ?,
                       heartbeat_at = ?, updated_at = ?
                   WHERE attempt_id = ? AND state = 'queued'""",
                (worker_id, token, expiry_text, now_text, now_text, row["attempt_id"]),
            ).rowcount
            if not updated:
                return None
            claimed = connection.execute("SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (row["attempt_id"],)).fetchone()
        return self._attempt(claimed) if claimed else None

    def heartbeat_attempt(self, attempt_id: str, worker_id: str, lease_expires_at: str, *, lease_token: str | None = None) -> DynamicAttemptRecord:
        if not worker_id:
            raise ValueError("attempt heartbeat requires a worker identity")
        expiry = _utc_timestamp(lease_expires_at, "lease_expires_at")
        now_text = utc_now()
        now = _utc_timestamp(now_text, "current time")
        if expiry <= now:
            raise ValueError("lease_expires_at must be in the future")
        expiry_text = expiry.isoformat()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT lease_token FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
            if row is None or not lease_token or row["lease_token"] != lease_token:
                raise ValueError("attempt lease token is stale or invalid")
            updated = connection.execute(
                """UPDATE dynamic_analysis_attempts
                   SET lease_expires_at = ?, heartbeat_at = ?, updated_at = ?
                   WHERE attempt_id = ? AND state = 'running' AND worker_id = ?
                     AND lease_token = ? AND lease_expires_at > ?""",
                (expiry_text, now_text, now_text, attempt_id, worker_id, lease_token, now_text),
            ).rowcount
            if not updated:
                raise ValueError("attempt lease is expired or not owned by this running worker")
            current = connection.execute("SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
        assert current is not None
        return self._attempt(current)

    def finish_attempt(self, attempt_id: str, worker_id: str, state: str, *, error: dict[str, Any] | None = None, lease_token: str | None = None) -> DynamicAttemptRecord:
        if state not in {"succeeded", "failed", "cancelled"}:
            raise ValueError("invalid terminal attempt state")
        if state == "failed" and error is None:
            raise ValueError("failed attempt requires a structured error")
        now_text = utc_now()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute("SELECT lease_token FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
            if row is None or not lease_token or row["lease_token"] != lease_token:
                raise ValueError("attempt lease token is stale or invalid")
            updated = connection.execute(
                """UPDATE dynamic_analysis_attempts SET state = ?, error_json = ?,
                   lease_expires_at = NULL, updated_at = ?
                   WHERE attempt_id = ? AND state = 'running' AND worker_id = ?
                     AND lease_token = ? AND lease_expires_at > ?""",
                (state, _json(error) if error is not None else None, now_text, attempt_id, worker_id, lease_token, now_text),
            ).rowcount
            if not updated:
                raise ValueError("attempt lease is expired or not owned by this running worker")
            current = connection.execute("SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?", (attempt_id,)).fetchone()
        assert current is not None
        return self._attempt(current)

    def update_attempt_window(self, attempt_id: str, worker_id: str, lease_token: str,
                              target: str, *, quality: str = "pending",
                              failure: dict[str, Any] | None = None,
                              artifact_id: str | None = None, manifest_sha256: str | None = None) -> None:
        """Fence every worker-owned state/publication update in one transaction."""
        if target == "ready" and not artifact_id:
            raise ValueError("ready dynamic window requires a verified artifact identity")
        if target in {"failed", "rejected", "unavailable"} and not failure:
            raise ValueError("terminal dynamic window requires a structured failure")
        now = utc_now()
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            attempt = connection.execute(
                """SELECT * FROM dynamic_analysis_attempts WHERE attempt_id = ?
                   AND state = 'running' AND worker_id = ? AND lease_token = ?
                   AND lease_expires_at > ?""", (attempt_id, worker_id, lease_token, now),
            ).fetchone()
            if attempt is None:
                raise ValueError("attempt lease is stale, expired or not owned by this worker")
            window = connection.execute(
                "SELECT * FROM dynamic_analysis_windows WHERE window_id = ?", (attempt["window_id"],),
            ).fetchone()
            if target == "cancelled" and window["state"] == "cancelled":
                pass  # Last-subscriber cancellation already committed the window.
            else:
                require_transition(window["state"], target)
                connection.execute(
                    """UPDATE dynamic_analysis_windows SET state = ?, quality = ?,
                       failure_json = ?, result_artifact_id = ?, result_manifest_sha256 = ?, updated_at = ?,
                       completed_at = ? WHERE window_id = ?""",
                    (target, quality, _json(failure) if failure else None, artifact_id, manifest_sha256,
                     now, now if target in TERMINAL_WINDOW_STATES else None, attempt["window_id"]),
                )
            if target in TERMINAL_WINDOW_STATES:
                attempt_state = "cancelled" if target == "cancelled" else "failed" if target == "failed" else "succeeded"
                connection.execute(
                    """UPDATE dynamic_analysis_attempts SET state = ?, error_json = ?,
                       lease_expires_at = NULL, updated_at = ? WHERE attempt_id = ?""",
                    (attempt_state, _json(failure) if failure else None, now, attempt_id),
                )

    def recover_expired_attempts(self) -> int:
        """Fence expired workers and queue a fresh attempt for each live window."""
        now_text = utc_now()
        now = _utc_timestamp(now_text, "current time")
        recovered = 0
        with self._connect() as connection:
            connection.execute("BEGIN IMMEDIATE")
            rows = connection.execute(
                """SELECT a.* FROM dynamic_analysis_attempts a
                   JOIN dynamic_analysis_windows w ON w.window_id = a.window_id
                   WHERE a.state = 'running' AND a.lease_expires_at IS NOT NULL
                     AND a.lease_expires_at <= ? AND w.state NOT IN ('ready','insufficient_data','rejected','unavailable','cancelled','failed')""", (now_text,),
            ).fetchall()
            for row in rows:
                connection.execute(
                    "UPDATE dynamic_analysis_attempts SET state = 'expired', worker_id = NULL, lease_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL, updated_at = ? WHERE attempt_id = ?",
                    (now_text, row["attempt_id"]),
                )
                connection.execute(
                    """UPDATE dynamic_analysis_windows
                       SET state = 'queued', quality = 'pending', failure_json = NULL,
                           updated_at = ?
                       WHERE window_id = ? AND state IN ('preparing_source', 'computing_window')""",
                    (now_text, row["window_id"]),
                )
                connection.execute(
                    """INSERT INTO dynamic_analysis_attempts(attempt_id, window_id, request_id, state, created_at, updated_at)
                       VALUES (?, ?, ?, 'queued', ?, ?)""",
                    (uuid4().hex, row["window_id"], f"recovery-{uuid4().hex}", now_text, now_text),
                )
                recovered += 1
        return recovered

    def _expire_running(self, connection, window_id: str, now: datetime) -> None:
        now_text = now.isoformat()
        rows = connection.execute(
            "SELECT * FROM dynamic_analysis_attempts WHERE window_id = ? AND state = 'running' AND lease_expires_at <= ?",
            (window_id, now_text),
        ).fetchall()
        for row in rows:
            connection.execute(
                "UPDATE dynamic_analysis_attempts SET state = 'expired', worker_id = NULL, lease_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL, updated_at = ? WHERE attempt_id = ?",
                (now_text, row["attempt_id"]),
            )
            connection.execute(
                """UPDATE dynamic_analysis_windows
                   SET state = 'queued', quality = 'pending', failure_json = NULL,
                       updated_at = ?
                   WHERE window_id = ? AND state IN ('preparing_source', 'computing_window')""",
                (now_text, row["window_id"]),
            )
            connection.execute(
                """INSERT INTO dynamic_analysis_attempts(attempt_id, window_id, request_id, state, created_at, updated_at)
                   VALUES (?, ?, ?, 'queued', ?, ?)""",
                (uuid4().hex, window_id, f"recovery-{uuid4().hex}", now_text, now_text),
            )

    @staticmethod
    def _attempt(row: sqlite3.Row) -> DynamicAttemptRecord:
        return DynamicAttemptRecord(
            attempt_id=row["attempt_id"], window_id=row["window_id"], request_id=row["request_id"],
            state=row["state"], worker_id=row["worker_id"], lease_token=row["lease_token"],
            lease_expires_at=row["lease_expires_at"], heartbeat_at=row["heartbeat_at"],
            error=_loads(row["error_json"]), created_at=row["created_at"], updated_at=row["updated_at"],
            producer_mode=row["producer_mode"] if "producer_mode" in row.keys() else "on_demand",
            producer_owner_id=row["producer_owner_id"] if "producer_owner_id" in row.keys() else None,
            priority=int(row["priority"]) if "priority" in row.keys() else 0,
        )

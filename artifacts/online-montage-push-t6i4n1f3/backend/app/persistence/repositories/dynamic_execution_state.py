"""Worker-owned, live cancellation and lease reads without connection churn."""

from __future__ import annotations

from pathlib import Path
from typing import Literal

from app.persistence import connect_database
from app.persistence.clock import utc_now
from .dynamic_attempt import _utc_timestamp


class DynamicExecutionStateReader:
    """One worker thread owns this connection; each SELECT sees current commits."""

    def __init__(self, database_path: Path, window_id: str,
                 attempt_id: str, lease_token: str) -> None:
        self._identity = (attempt_id, window_id)
        self._lease_token = lease_token
        self._connection = connect_database(database_path, autocommit=True)

    def read(self) -> Literal["active", "cancelled", "lease_lost"]:
        """Check the exact window/attempt/token together, including UTC expiry."""
        row = self._connection.execute(
            """SELECT w.state AS window_state, a.state AS attempt_state,
                      a.lease_token, a.lease_expires_at
               FROM dynamic_analysis_windows w
               LEFT JOIN dynamic_analysis_attempts a
                 ON a.window_id = w.window_id AND a.attempt_id = ?
               WHERE w.window_id = ?""", self._identity,
        ).fetchone()
        if row is None or row["window_state"] == "cancelled":
            return "cancelled"
        if (row["attempt_state"] != "running" or row["lease_token"] != self._lease_token
                or not row["lease_expires_at"]):
            return "lease_lost"
        if (_utc_timestamp(row["lease_expires_at"], "lease_expires_at")
                <= _utc_timestamp(utc_now(), "current time")):
            return "lease_lost"
        return "active"

    def close(self) -> None:
        self._connection.close()

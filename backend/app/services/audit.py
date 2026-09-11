"""本地操作审计存储；不包含原始 EEG 样本或患者内容。"""

from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH


class AuditService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as connection:
            connection.execute(
                """CREATE TABLE IF NOT EXISTS audit_events (
                    id TEXT PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    action TEXT NOT NULL,
                    outcome TEXT NOT NULL,
                    recording_id TEXT,
                    session_id TEXT,
                    request_id TEXT NOT NULL,
                    actor_id TEXT,
                    parameters_json TEXT NOT NULL
                )"""
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def record(self, action: str, request_id: str, *, outcome: str = "success", recording_id: str | None = None,
               session_id: str | None = None, actor_id: str | None = None, parameters: dict[str, object] | None = None) -> str:
        event_id = uuid4().hex
        payload = json.dumps(parameters or {}, ensure_ascii=False, sort_keys=True)
        with self._connect() as connection:
            connection.execute(
                """INSERT INTO audit_events
                   (id, occurred_at, action, outcome, recording_id, session_id, request_id, actor_id, parameters_json)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (event_id, datetime.now(timezone.utc).isoformat(), action, outcome, recording_id, session_id,
                 str(request_id or "unknown"), actor_id, payload),
            )
        return event_id

    def list_events(self, recording_id: str | None = None, limit: int = 100) -> list[dict[str, object]]:
        bounded_limit = max(1, min(int(limit), 1000))
        query = "SELECT * FROM audit_events"
        values: list[object] = []
        if recording_id:
            query += " WHERE recording_id = ?"
            values.append(recording_id)
        query += " ORDER BY occurred_at DESC LIMIT ?"
        values.append(bounded_limit)
        with self._connect() as connection:
            rows = connection.execute(query, values).fetchall()
        return [{**dict(row), "parameters": json.loads(row["parameters_json"])} for row in rows]

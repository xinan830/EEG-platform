"""本地操作审计存储；不包含原始 EEG 样本或患者内容。"""

from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.persistence import connect_database, migrate_database


class AuditService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path)

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

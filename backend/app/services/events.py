"""录制文件人工事件标记的持久化服务。"""

from __future__ import annotations

import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH


class EventMarkerService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as connection:
            connection.execute(
                """CREATE TABLE IF NOT EXISTS event_markers (
                    id TEXT PRIMARY KEY,
                    recording_id TEXT NOT NULL,
                    time_s REAL NOT NULL,
                    label TEXT NOT NULL,
                    duration_s REAL,
                    created_at TEXT NOT NULL
                )"""
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def create(self, recording_id: str, time_s: float, label: str, duration_s: float | None = None) -> dict[str, object]:
        marker_id = uuid4().hex
        created_at = datetime.now(timezone.utc).isoformat()
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO event_markers (id, recording_id, time_s, label, duration_s, created_at) VALUES (?, ?, ?, ?, ?, ?)",
                (marker_id, recording_id, time_s, label, duration_s, created_at),
            )
        return {"id": marker_id, "recording_id": recording_id, "time_s": time_s, "label": label, "duration_s": duration_s, "created_at": created_at}

    def list_markers(self, recording_id: str) -> list[dict[str, object]]:
        with self._connect() as connection:
            rows = connection.execute("SELECT * FROM event_markers WHERE recording_id = ? ORDER BY time_s, created_at", (recording_id,)).fetchall()
        return [dict(row) for row in rows]

    def delete(self, recording_id: str, marker_id: str) -> bool:
        with self._connect() as connection:
            cursor = connection.execute("DELETE FROM event_markers WHERE id = ? AND recording_id = ?", (marker_id, recording_id))
        return cursor.rowcount == 1

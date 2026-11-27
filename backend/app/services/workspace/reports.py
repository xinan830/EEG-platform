"""阅图报告快照；只保存可追溯元数据，不复制原始 EEG 样本。"""

from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import DATABASE_PATH
from app.persistence import connect_database, migrate_database


class ReportSnapshotService:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path)

    def create(self, recording_id: str, title: str, payload: dict[str, object]) -> dict[str, object]:
        report_id = uuid4().hex
        created_at = datetime.now(timezone.utc).isoformat()
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO report_snapshots (id, recording_id, title, created_at, payload_json) VALUES (?, ?, ?, ?, ?)",
                (report_id, recording_id, title, created_at, json.dumps(payload, ensure_ascii=False, sort_keys=True)),
            )
        return {"id": report_id, "recording_id": recording_id, "title": title, "created_at": created_at, "payload": payload}

    def list_reports(self, recording_id: str) -> list[dict[str, object]]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT id, recording_id, title, created_at, payload_json FROM report_snapshots WHERE recording_id = ? ORDER BY created_at DESC",
                (recording_id,),
            ).fetchall()
        return [self._serialize(row) for row in rows]

    def get(self, recording_id: str, report_id: str) -> dict[str, object] | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT id, recording_id, title, created_at, payload_json FROM report_snapshots WHERE id = ? AND recording_id = ?",
                (report_id, recording_id),
            ).fetchone()
        return self._serialize(row) if row else None

    @staticmethod
    def _serialize(row: sqlite3.Row) -> dict[str, object]:
        result = dict(row)
        result["payload"] = json.loads(result.pop("payload_json"))
        return result

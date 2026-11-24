"""SQLite persistence for official algorithm parameter presets."""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from app.core.config import DATABASE_PATH
from app.models.algorithm_preset import AlgorithmPreset
from app.persistence import connect_database, migrate_database


class AlgorithmPresetRepository:
    def __init__(self, database_path: Path = DATABASE_PATH):
        self.database_path = Path(database_path)
        migrate_database(self.database_path)

    def _connect(self) -> sqlite3.Connection:
        return connect_database(self.database_path, foreign_keys=True)

    def create(self, preset: AlgorithmPreset) -> None:
        with self._connect() as connection:
            connection.execute(
                """INSERT INTO algorithm_parameter_presets
                   (preset_id, algorithm_id, scientific_version, name, config_json,
                    config_sha256, created_at, updated_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
                (preset.preset_id, preset.algorithm_id, preset.scientific_version,
                 preset.name, json.dumps(preset.config, ensure_ascii=False, sort_keys=True),
                 preset.config_sha256, preset.created_at, preset.updated_at),
            )

    def get(self, preset_id: str) -> AlgorithmPreset | None:
        with self._connect() as connection:
            row = connection.execute("SELECT * FROM algorithm_parameter_presets WHERE preset_id = ?", (preset_id,)).fetchone()
        return self._row(row) if row else None

    def list(self, algorithm_id: str | None = None) -> list[AlgorithmPreset]:
        query = "SELECT * FROM algorithm_parameter_presets"
        values: tuple[object, ...] = ()
        if algorithm_id:
            query += " WHERE algorithm_id = ?"
            values = (algorithm_id,)
        query += " ORDER BY updated_at DESC"
        with self._connect() as connection:
            rows = connection.execute(query, values).fetchall()
        return [self._row(row) for row in rows]

    def update(self, preset: AlgorithmPreset) -> None:
        with self._connect() as connection:
            connection.execute(
                """UPDATE algorithm_parameter_presets
                   SET name = ?, config_json = ?, config_sha256 = ?, updated_at = ?
                   WHERE preset_id = ?""",
                (preset.name, json.dumps(preset.config, ensure_ascii=False, sort_keys=True),
                 preset.config_sha256, preset.updated_at, preset.preset_id),
            )

    def delete(self, preset_id: str) -> bool:
        with self._connect() as connection:
            return connection.execute("DELETE FROM algorithm_parameter_presets WHERE preset_id = ?", (preset_id,)).rowcount > 0

    @staticmethod
    def _row(row: sqlite3.Row) -> AlgorithmPreset:
        return AlgorithmPreset(
            preset_id=row["preset_id"], algorithm_id=row["algorithm_id"],
            scientific_version=row["scientific_version"], name=row["name"],
            config=json.loads(row["config_json"]), config_sha256=row["config_sha256"],
            created_at=row["created_at"], updated_at=row["updated_at"],
        )

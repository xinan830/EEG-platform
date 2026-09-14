import sqlite3
from pathlib import Path

import pytest

from app.persistence.migrations import (
    CURRENT_SCHEMA_VERSION,
    Migration,
    get_schema_version,
    migrate_database,
)


def test_migration_upgrades_legacy_database_without_losing_rows(tmp_path: Path):
    database = tmp_path / "legacy.sqlite3"
    with sqlite3.connect(database) as connection:
        connection.execute(
            """CREATE TABLE recordings (
               id TEXT PRIMARY KEY, original_name TEXT NOT NULL, stored_name TEXT NOT NULL UNIQUE,
               extension TEXT NOT NULL, created_at TEXT NOT NULL, sfreq REAL, duration_s REAL,
               channels_json TEXT NOT NULL DEFAULT '[]', mapping_json TEXT)"""
        )
        connection.execute(
            "INSERT INTO recordings VALUES ('r1', 'a.edf', 'r1.edf', '.edf', 'now', 500, 2, '[\"F3\"]', NULL)"
        )

    assert migrate_database(database) == CURRENT_SCHEMA_VERSION
    assert migrate_database(database) == CURRENT_SCHEMA_VERSION

    with sqlite3.connect(database) as connection:
        row = connection.execute(
            "SELECT id, original_name, channels_json FROM recordings WHERE id = 'r1'"
        ).fetchone()
        columns = {item[1] for item in connection.execute("PRAGMA table_info(recordings)")}
        validation_columns = {item[1] for item in connection.execute("PRAGMA table_info(validation_runs)")}
    assert row == ("r1", "a.edf", '["F3"]')
    assert {"source_sha256", "file_size_bytes", "raw_channel_labels_json"} <= columns
    assert "evidence_json" in validation_columns
    assert get_schema_version(database) == CURRENT_SCHEMA_VERSION


def test_failed_migration_rolls_back_schema_and_version(tmp_path: Path):
    database = tmp_path / "failure.sqlite3"
    migrate_database(database)

    def fail(connection: sqlite3.Connection) -> None:
        connection.execute("CREATE TABLE must_rollback (id TEXT)")
        raise RuntimeError("injected migration failure")

    with pytest.raises(RuntimeError, match="injected"):
        migrate_database(database, [Migration(CURRENT_SCHEMA_VERSION + 1, "fail", fail)])

    with sqlite3.connect(database) as connection:
        table = connection.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='must_rollback'"
        ).fetchone()
    assert table is None
    assert get_schema_version(database) == CURRENT_SCHEMA_VERSION

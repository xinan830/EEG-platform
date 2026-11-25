import sqlite3
from pathlib import Path

import pytest

from app.persistence.migrations import (
    CURRENT_SCHEMA_VERSION,
    Migration,
    get_schema_version,
    migrate_database,
)
from app.persistence import connect_database


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


def test_migration_22_to_23_adds_nullable_import_binding_columns(tmp_path: Path):
    database = tmp_path / "channel-binding.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute("ALTER TABLE recordings DROP COLUMN import_channel_configuration_snapshot_json")
        connection.execute("ALTER TABLE recordings DROP COLUMN import_channel_column_mapping_json")
        connection.execute("UPDATE schema_metadata SET value = '22' WHERE key = 'schema_version'")

    assert migrate_database(database) == 23
    with sqlite3.connect(database) as connection:
        columns = {row[1]: row for row in connection.execute("PRAGMA table_info(recordings)")}
    assert columns["import_channel_configuration_snapshot_json"][3] == 0
    assert columns["import_channel_column_mapping_json"][3] == 0


def test_shared_connection_policy_preserves_row_access_and_opt_in_foreign_keys(tmp_path: Path):
    database = tmp_path / "connections.sqlite3"
    with connect_database(database, foreign_keys=True) as connection:
        assert connection.execute("PRAGMA foreign_keys").fetchone()[0] == 1
        assert connection.execute("PRAGMA busy_timeout").fetchone()[0] == 5000
        connection.execute("CREATE TABLE values_table (id INTEGER PRIMARY KEY, value TEXT)")
        connection.execute("INSERT INTO values_table (value) VALUES ('ok')")
        row = connection.execute("SELECT value FROM values_table").fetchone()
    assert row["value"] == "ok"

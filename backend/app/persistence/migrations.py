"""Ordered, transactional SQLite migrations for the local workstation."""

from __future__ import annotations

import sqlite3
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from pathlib import Path


MigrationAction = Callable[[sqlite3.Connection], None]


@dataclass(frozen=True)
class Migration:
    version: int
    name: str
    apply: MigrationAction


def _column_names(connection: sqlite3.Connection, table: str) -> set[str]:
    return {str(row[1]) for row in connection.execute(f"PRAGMA table_info({table})")}


def _add_column(connection: sqlite3.Connection, table: str, declaration: str) -> None:
    column = declaration.split(maxsplit=1)[0]
    if column not in _column_names(connection, table):
        connection.execute(f"ALTER TABLE {table} ADD COLUMN {declaration}")


def _migration_001_legacy_tables(connection: sqlite3.Connection) -> None:
    statements = (
        """CREATE TABLE IF NOT EXISTS recordings (
            id TEXT PRIMARY KEY,
            original_name TEXT NOT NULL,
            stored_name TEXT NOT NULL UNIQUE,
            extension TEXT NOT NULL,
            created_at TEXT NOT NULL,
            sfreq REAL,
            duration_s REAL,
            channels_json TEXT NOT NULL DEFAULT '[]',
            mapping_json TEXT
        )""",
        """CREATE TABLE IF NOT EXISTS analyses (
            analysis_id TEXT PRIMARY KEY,
            recording_id TEXT NOT NULL,
            status TEXT NOT NULL,
            result_json TEXT NOT NULL
        )""",
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
        )""",
        """CREATE TABLE IF NOT EXISTS event_markers (
            id TEXT PRIMARY KEY,
            recording_id TEXT NOT NULL,
            time_s REAL NOT NULL,
            label TEXT NOT NULL,
            duration_s REAL,
            created_at TEXT NOT NULL
        )""",
        """CREATE TABLE IF NOT EXISTS report_snapshots (
            id TEXT PRIMARY KEY,
            recording_id TEXT NOT NULL,
            title TEXT NOT NULL,
            created_at TEXT NOT NULL,
            payload_json TEXT NOT NULL
        )""",
    )
    for statement in statements:
        connection.execute(statement)


def _migration_002_recording_identity(connection: sqlite3.Connection) -> None:
    additions = (
        "source_sha256 TEXT",
        "file_size_bytes INTEGER",
        "raw_channel_labels_json TEXT NOT NULL DEFAULT '[]'",
        "canonical_channel_labels_json TEXT NOT NULL DEFAULT '[]'",
        "channel_types_json TEXT NOT NULL DEFAULT '[]'",
        "channel_units_json TEXT NOT NULL DEFAULT '[]'",
        "import_version TEXT NOT NULL DEFAULT 'legacy-unversioned'",
    )
    for declaration in additions:
        _add_column(connection, "recordings", declaration)


def _migration_003_run_foundation(connection: sqlite3.Connection) -> None:
    statements = (
        """CREATE TABLE IF NOT EXISTS analysis_runs (
            run_id TEXT PRIMARY KEY,
            recording_id TEXT NOT NULL,
            analysis_type TEXT NOT NULL,
            status TEXT NOT NULL,
            definition_id TEXT,
            definition_version TEXT,
            scientific_version TEXT NOT NULL,
            implementation_version TEXT NOT NULL,
            config_json TEXT NOT NULL,
            config_sha256 TEXT NOT NULL,
            cache_key TEXT NOT NULL,
            requested_range_json TEXT NOT NULL,
            actual_range_json TEXT,
            channel_mapping_json TEXT NOT NULL,
            reference_json TEXT NOT NULL,
            filter_json TEXT NOT NULL,
            window_json TEXT NOT NULL,
            quality_rules_json TEXT NOT NULL,
            environment_json TEXT NOT NULL,
            result_summary_json TEXT,
            error_json TEXT,
            is_preview INTEGER NOT NULL DEFAULT 0,
            reused_from_run_id TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            started_at TEXT,
            completed_at TEXT
        )""",
        """CREATE INDEX IF NOT EXISTS idx_analysis_runs_recording_created
            ON analysis_runs(recording_id, created_at DESC)""",
        """CREATE INDEX IF NOT EXISTS idx_analysis_runs_cache
            ON analysis_runs(cache_key, status)""",
        """CREATE TABLE IF NOT EXISTS run_artifacts (
            artifact_id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            relative_path TEXT NOT NULL UNIQUE,
            media_type TEXT NOT NULL,
            byte_size INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            unit TEXT,
            shape_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY(run_id) REFERENCES analysis_runs(run_id)
        )""",
        """CREATE INDEX IF NOT EXISTS idx_run_artifacts_run ON run_artifacts(run_id, created_at)""",
        """CREATE TABLE IF NOT EXISTS validation_runs (
            validation_id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            status TEXT NOT NULL,
            subject_run_id TEXT,
            algorithm_id TEXT,
            algorithm_version TEXT,
            dataset_identity_json TEXT NOT NULL,
            config_sha256 TEXT NOT NULL,
            tolerances_json TEXT NOT NULL,
            expected_summary_json TEXT,
            actual_summary_json TEXT,
            max_absolute_error REAL,
            max_relative_error REAL,
            point_count INTEGER NOT NULL DEFAULT 0,
            passed_point_count INTEGER NOT NULL DEFAULT 0,
            pass_rate REAL,
            passed INTEGER,
            environment_json TEXT NOT NULL,
            error_json TEXT,
            created_at TEXT NOT NULL,
            completed_at TEXT
        )""",
        """CREATE INDEX IF NOT EXISTS idx_validation_runs_created
            ON validation_runs(created_at DESC)""",
    )
    for statement in statements:
        connection.execute(statement)


MIGRATIONS: tuple[Migration, ...] = (
    Migration(1, "legacy-tables", _migration_001_legacy_tables),
    Migration(2, "recording-identity", _migration_002_recording_identity),
    Migration(3, "run-foundation", _migration_003_run_foundation),
)
CURRENT_SCHEMA_VERSION = MIGRATIONS[-1].version


def _connect(database_path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(database_path, timeout=5.0, isolation_level=None)
    connection.execute("PRAGMA busy_timeout = 5000")
    connection.execute("PRAGMA foreign_keys = ON")
    return connection


def _ensure_metadata(connection: sqlite3.Connection) -> None:
    connection.execute(
        "CREATE TABLE IF NOT EXISTS schema_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)"
    )
    connection.execute(
        "INSERT OR IGNORE INTO schema_metadata (key, value) VALUES ('schema_version', '0')"
    )


def get_schema_version(database_path: Path) -> int:
    path = Path(database_path)
    if not path.exists():
        return 0
    with _connect(path) as connection:
        row = connection.execute(
            "SELECT value FROM schema_metadata WHERE key = 'schema_version'"
        ).fetchone() if "schema_metadata" in {
            item[0] for item in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")
        } else None
    return int(row[0]) if row else 0


def migrate_database(
    database_path: Path,
    migrations: Sequence[Migration] = MIGRATIONS,
) -> int:
    """Apply pending migrations one transaction at a time and return the version."""
    path = Path(database_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    connection = _connect(path)
    try:
        _ensure_metadata(connection)
        current = int(connection.execute(
            "SELECT value FROM schema_metadata WHERE key = 'schema_version'"
        ).fetchone()[0])
        for migration in sorted(migrations, key=lambda item: item.version):
            if migration.version <= current:
                continue
            connection.execute("BEGIN IMMEDIATE")
            try:
                migration.apply(connection)
                connection.execute(
                    "UPDATE schema_metadata SET value = ? WHERE key = 'schema_version'",
                    (str(migration.version),),
                )
                connection.execute("COMMIT")
            except Exception:
                connection.execute("ROLLBACK")
                raise
            current = migration.version
        return current
    finally:
        connection.close()

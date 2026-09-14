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


def _migration_004_algorithm_definitions(connection: sqlite3.Connection) -> None:
    statements = (
        """CREATE TABLE IF NOT EXISTS algorithm_definitions (
            definition_id TEXT PRIMARY KEY, name TEXT NOT NULL, owner TEXT NOT NULL,
            status TEXT NOT NULL, description TEXT NOT NULL, created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL)""",
        """CREATE TABLE IF NOT EXISTS algorithm_definition_versions (
            version_id TEXT PRIMARY KEY, definition_id TEXT NOT NULL, semver TEXT NOT NULL,
            state TEXT NOT NULL, graph_json TEXT NOT NULL, parameter_schema_json TEXT NOT NULL,
            inputs_json TEXT NOT NULL, outputs_json TEXT NOT NULL, units_json TEXT NOT NULL,
            quality_rules_json TEXT NOT NULL, references_json TEXT NOT NULL, digest_sha256 TEXT NOT NULL,
            created_at TEXT NOT NULL, published_at TEXT,
            UNIQUE(definition_id, semver), UNIQUE(definition_id, digest_sha256),
            FOREIGN KEY(definition_id) REFERENCES algorithm_definitions(definition_id))""",
        "CREATE INDEX IF NOT EXISTS idx_algorithm_definitions_updated ON algorithm_definitions(updated_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_algorithm_versions_definition ON algorithm_definition_versions(definition_id, created_at DESC)",
    )
    for statement in statements:
        connection.execute(statement)


def _migration_005_research_projects(connection: sqlite3.Connection) -> None:
    statements = (
        """CREATE TABLE IF NOT EXISTS projects (
            project_id TEXT PRIMARY KEY, name TEXT NOT NULL, description TEXT NOT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL)""",
        """CREATE TABLE IF NOT EXISTS subjects (
            subject_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, local_code TEXT NOT NULL,
            created_at TEXT NOT NULL, UNIQUE(project_id, local_code),
            FOREIGN KEY(project_id) REFERENCES projects(project_id))""",
        """CREATE TABLE IF NOT EXISTS conditions (
            condition_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, code TEXT NOT NULL,
            label TEXT NOT NULL, created_at TEXT NOT NULL, UNIQUE(project_id, code),
            FOREIGN KEY(project_id) REFERENCES projects(project_id))""",
        """CREATE TABLE IF NOT EXISTS sessions (
            session_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, subject_id TEXT NOT NULL,
            recording_id TEXT NOT NULL, condition_id TEXT, label TEXT NOT NULL, created_at TEXT NOT NULL,
            FOREIGN KEY(project_id) REFERENCES projects(project_id),
            FOREIGN KEY(subject_id) REFERENCES subjects(subject_id),
            FOREIGN KEY(condition_id) REFERENCES conditions(condition_id),
            FOREIGN KEY(recording_id) REFERENCES recordings(id))""",
        "CREATE INDEX IF NOT EXISTS idx_subjects_project ON subjects(project_id, local_code)",
        "CREATE INDEX IF NOT EXISTS idx_sessions_project ON sessions(project_id, created_at)",
        "CREATE INDEX IF NOT EXISTS idx_sessions_recording ON sessions(recording_id)",
    )
    for statement in statements:
        connection.execute(statement)


def _migration_006_persistent_run_queue(connection: sqlite3.Connection) -> None:
    additions = (
        "project_id TEXT",
        "batch_run_id TEXT",
        "idempotency_key TEXT",
        "parent_run_id TEXT",
        "cancel_requested INTEGER NOT NULL DEFAULT 0",
    )
    for declaration in additions:
        _add_column(connection, "analysis_runs", declaration)
    statements = (
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_analysis_runs_idempotency ON analysis_runs(idempotency_key) WHERE idempotency_key IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS idx_analysis_runs_queue ON analysis_runs(status, created_at)",
        """CREATE TABLE IF NOT EXISTS batch_runs (
            batch_run_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, analysis_type TEXT NOT NULL,
            definition_id TEXT, definition_version TEXT, config_json TEXT NOT NULL,
            config_sha256 TEXT NOT NULL, idempotency_key TEXT UNIQUE, status TEXT NOT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            FOREIGN KEY(project_id) REFERENCES projects(project_id))""",
        """CREATE TABLE IF NOT EXISTS batch_run_items (
            batch_run_id TEXT NOT NULL, recording_id TEXT NOT NULL, run_id TEXT,
            outcome TEXT NOT NULL, error_code TEXT,
            PRIMARY KEY(batch_run_id, recording_id),
            FOREIGN KEY(batch_run_id) REFERENCES batch_runs(batch_run_id),
            FOREIGN KEY(run_id) REFERENCES analysis_runs(run_id))""",
        "CREATE INDEX IF NOT EXISTS idx_batch_items_run ON batch_run_items(run_id)",
    )
    for statement in statements:
        connection.execute(statement)


MIGRATIONS: tuple[Migration, ...] = (
    Migration(1, "legacy-tables", _migration_001_legacy_tables),
    Migration(2, "recording-identity", _migration_002_recording_identity),
    Migration(3, "run-foundation", _migration_003_run_foundation),
    Migration(4, "algorithm-definitions", _migration_004_algorithm_definitions),
    Migration(5, "research-projects", _migration_005_research_projects),
    Migration(6, "persistent-run-queue", _migration_006_persistent_run_queue),
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

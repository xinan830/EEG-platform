"""SQLite schema for the opt-in on-demand dynamic-window store."""

from __future__ import annotations

import sqlite3


def migration_014_on_demand_dynamic_windows(connection: sqlite3.Connection) -> None:
    """Create isolated storage for immutable on-demand window attempts."""
    statements = (
        """CREATE TABLE IF NOT EXISTS dynamic_analysis_sessions (
            session_id TEXT PRIMARY KEY, project_id TEXT, recording_id TEXT NOT NULL,
            source_sha256 TEXT NOT NULL, algorithm_id TEXT NOT NULL, definition_id TEXT NOT NULL,
            definition_version TEXT NOT NULL, scientific_version TEXT NOT NULL,
            implementation_identity TEXT NOT NULL, configuration_sha256 TEXT NOT NULL,
            configuration_json TEXT NOT NULL, window_policy_version TEXT NOT NULL,
            status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            FOREIGN KEY(project_id) REFERENCES projects(project_id),
            FOREIGN KEY(recording_id) REFERENCES recordings(id),
            UNIQUE(project_id, recording_id, source_sha256, algorithm_id, definition_id,
                   definition_version, scientific_version, implementation_identity,
                   configuration_sha256, window_policy_version)
        )""",
        """CREATE TABLE IF NOT EXISTS dynamic_analysis_windows (
            window_id TEXT PRIMARY KEY, session_id TEXT NOT NULL,
            start_sample INTEGER NOT NULL, end_sample INTEGER NOT NULL,
            start_s REAL NOT NULL, end_s REAL NOT NULL, state TEXT NOT NULL,
            quality TEXT NOT NULL, failure_json TEXT, result_artifact_id TEXT,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT,
            FOREIGN KEY(session_id) REFERENCES dynamic_analysis_sessions(session_id),
            UNIQUE(session_id, start_sample, end_sample), CHECK(start_sample >= 0),
            CHECK(end_sample > start_sample), CHECK(end_s > start_s)
        )""",
        """CREATE TABLE IF NOT EXISTS dynamic_analysis_attempts (
            attempt_id TEXT PRIMARY KEY, window_id TEXT NOT NULL, request_id TEXT NOT NULL,
            state TEXT NOT NULL, worker_id TEXT, lease_expires_at TEXT,
            heartbeat_at TEXT, error_json TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            FOREIGN KEY(window_id) REFERENCES dynamic_analysis_windows(window_id)
        )""",
        """CREATE TABLE IF NOT EXISTS dynamic_analysis_subscriptions (
            subscription_id TEXT PRIMARY KEY, window_id TEXT NOT NULL, request_id TEXT NOT NULL,
            project_id TEXT, state TEXT NOT NULL, created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL, cancelled_at TEXT,
            FOREIGN KEY(window_id) REFERENCES dynamic_analysis_windows(window_id),
            FOREIGN KEY(project_id) REFERENCES projects(project_id)
        )""",
        """CREATE UNIQUE INDEX IF NOT EXISTS idx_dynamic_sessions_identity_unscoped
         ON dynamic_analysis_sessions(recording_id, source_sha256, algorithm_id, definition_id,
            definition_version, scientific_version, implementation_identity, configuration_sha256,
            window_policy_version) WHERE project_id IS NULL""",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_sessions_recording ON dynamic_analysis_sessions(recording_id, updated_at DESC)",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_sessions_identity ON dynamic_analysis_sessions(configuration_sha256, source_sha256)",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_windows_state ON dynamic_analysis_windows(state, updated_at)",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_windows_session ON dynamic_analysis_windows(session_id, start_sample)",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_attempts_active ON dynamic_analysis_attempts(window_id, state, lease_expires_at)",
        "CREATE INDEX IF NOT EXISTS idx_dynamic_subscriptions_active ON dynamic_analysis_subscriptions(window_id, state)",
    )
    for statement in statements:
        connection.execute(statement)


def migration_015_dynamic_attempt_lease_index(connection: sqlite3.Connection) -> None:
    """Prevent two workers from holding a lease for one window."""
    connection.execute(
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_dynamic_attempts_one_running "
        "ON dynamic_analysis_attempts(window_id) WHERE state = 'running'"
    )


def migration_016_dynamic_attempt_fencing(connection: sqlite3.Connection) -> None:
    """Fence recovered workers and coalesce queued work per window."""
    columns = {row[1] for row in connection.execute("PRAGMA table_info(dynamic_analysis_attempts)")}
    if "lease_token" not in columns:
        connection.execute("ALTER TABLE dynamic_analysis_attempts ADD COLUMN lease_token TEXT")
    connection.execute(
        """UPDATE dynamic_analysis_attempts
           SET state = 'superseded', updated_at = updated_at
           WHERE state = 'queued' AND attempt_id NOT IN (
               SELECT MIN(attempt_id) FROM dynamic_analysis_attempts
               WHERE state = 'queued' GROUP BY window_id
           )"""
    )
    connection.execute(
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_dynamic_attempts_one_active "
        "ON dynamic_analysis_attempts(window_id) WHERE state IN ('queued', 'running')"
    )


def migration_017_dynamic_window_generations(connection: sqlite3.Connection) -> None:
    """Allow explicit retries without reopening an immutable terminal window.

    v14 used a unique ``(session_id, start_sample, end_sample)`` constraint.
    Rebuilding this small isolated table is safer than dropping the constraint
    in place: all old rows receive generation zero and remain byte-for-byte
    readable, while later retries may use generation one, two, and so on.
    """
    columns = {row[1] for row in connection.execute("PRAGMA table_info(dynamic_analysis_windows)")}
    if "generation" not in columns:
        # Rebuild children inside the same transaction too. Merely deferring
        # FK checks while dropping their parent leaves SQLite's deferred
        # violation counter nonzero even after all parent IDs are restored.
        for table in ("dynamic_analysis_attempts", "dynamic_analysis_subscriptions"):
            connection.execute(f"CREATE TEMP TABLE {table}_backup AS SELECT * FROM {table}")
            connection.execute(f"DROP TABLE {table}")
        connection.execute(
            """CREATE TABLE dynamic_analysis_windows_v17 (
                window_id TEXT PRIMARY KEY, session_id TEXT NOT NULL,
                generation INTEGER NOT NULL DEFAULT 0,
                start_sample INTEGER NOT NULL, end_sample INTEGER NOT NULL,
                start_s REAL NOT NULL, end_s REAL NOT NULL, state TEXT NOT NULL,
                quality TEXT NOT NULL, failure_json TEXT, result_artifact_id TEXT,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT,
                FOREIGN KEY(session_id) REFERENCES dynamic_analysis_sessions(session_id),
                UNIQUE(session_id, start_sample, end_sample, generation),
                CHECK(generation >= 0), CHECK(start_sample >= 0),
                CHECK(end_sample > start_sample), CHECK(end_s > start_s)
            )"""
        )
        connection.execute(
            """INSERT INTO dynamic_analysis_windows_v17(
                window_id, session_id, generation, start_sample, end_sample,
                start_s, end_s, state, quality, failure_json, result_artifact_id,
                created_at, updated_at, completed_at
            ) SELECT window_id, session_id, 0, start_sample, end_sample,
                start_s, end_s, state, quality, failure_json, result_artifact_id,
                created_at, updated_at, completed_at
                FROM dynamic_analysis_windows"""
        )
        connection.execute("DROP TABLE dynamic_analysis_windows")
        connection.execute("ALTER TABLE dynamic_analysis_windows_v17 RENAME TO dynamic_analysis_windows")
        migration_014_on_demand_dynamic_windows(connection)
        migration_015_dynamic_attempt_lease_index(connection)
        migration_016_dynamic_attempt_fencing(connection)
        connection.execute("""INSERT INTO dynamic_analysis_attempts(
            attempt_id, window_id, request_id, state, worker_id, lease_expires_at,
            heartbeat_at, error_json, created_at, updated_at, lease_token
        ) SELECT attempt_id, window_id, request_id, state, worker_id, lease_expires_at,
            heartbeat_at, error_json, created_at, updated_at, lease_token
            FROM dynamic_analysis_attempts_backup""")
        connection.execute("""INSERT INTO dynamic_analysis_subscriptions(
            subscription_id, window_id, request_id, project_id, state,
            created_at, updated_at, cancelled_at
        ) SELECT subscription_id, window_id, request_id, project_id, state,
            created_at, updated_at, cancelled_at FROM dynamic_analysis_subscriptions_backup""")
        for table in ("dynamic_analysis_attempts", "dynamic_analysis_subscriptions"):
            connection.execute(f"DROP TABLE {table}_backup")
    connection.execute("CREATE INDEX IF NOT EXISTS idx_dynamic_windows_state ON dynamic_analysis_windows(state, updated_at)")
    connection.execute("CREATE INDEX IF NOT EXISTS idx_dynamic_windows_session ON dynamic_analysis_windows(session_id, start_sample, generation)")


def migration_018_dynamic_manifest_integrity(connection: sqlite3.Connection) -> None:
    columns = {row[1] for row in connection.execute("PRAGMA table_info(dynamic_analysis_windows)")}
    if "result_manifest_sha256" not in columns:
        connection.execute("ALTER TABLE dynamic_analysis_windows ADD COLUMN result_manifest_sha256 TEXT")

"""Upgrade isolated window-session identity without rewriting historical payloads."""

import sqlite3


def migration_019_dynamic_resolved_identity(connection: sqlite3.Connection) -> None:
    columns = {row[1] for row in connection.execute("PRAGMA table_info(dynamic_analysis_sessions)")}
    if "definition_sha256" in columns:
        return
    # SQLite cannot remove the original table-level uniqueness constraint.
    # Preserve all children and their exact schemas/indices in one transaction.
    tables = ("dynamic_analysis_sessions", "dynamic_analysis_windows",
              "dynamic_analysis_attempts", "dynamic_analysis_subscriptions")
    child_schemas = {}
    indexes = []
    for table in tables:
        if table != tables[0]:
            child_schemas[table] = connection.execute(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name=?", (table,),
            ).fetchone()[0]
        indexes.extend(connection.execute(
            "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name=? AND sql IS NOT NULL", (table,),
        ).fetchall())
        connection.execute(f"CREATE TEMP TABLE {table}_v19_backup AS SELECT * FROM {table}")
    for table in reversed(tables):
        connection.execute(f"DROP TABLE {table}")
    connection.execute("""CREATE TABLE dynamic_analysis_sessions (
        session_id TEXT PRIMARY KEY, project_id TEXT, recording_id TEXT NOT NULL,
        source_sha256 TEXT NOT NULL, algorithm_id TEXT NOT NULL, definition_id TEXT NOT NULL,
        definition_version TEXT NOT NULL, scientific_version TEXT NOT NULL,
        implementation_identity TEXT NOT NULL, configuration_sha256 TEXT NOT NULL,
        configuration_json TEXT NOT NULL, window_policy_version TEXT NOT NULL,
        status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
        definition_sha256 TEXT NOT NULL DEFAULT '', implementation_build TEXT NOT NULL DEFAULT '',
        FOREIGN KEY(project_id) REFERENCES projects(project_id),
        FOREIGN KEY(recording_id) REFERENCES recordings(id)
    )""")
    old_columns = ", ".join(row[1] for row in connection.execute(
        "PRAGMA table_info(dynamic_analysis_sessions_v19_backup)"))
    connection.execute(f"INSERT INTO dynamic_analysis_sessions({old_columns}) SELECT {old_columns} FROM dynamic_analysis_sessions_v19_backup")
    for table in tables[1:]:
        connection.execute(child_schemas[table])
        connection.execute(f"INSERT INTO {table} SELECT * FROM {table}_v19_backup")
    for name, sql in indexes:
        if name != "idx_dynamic_sessions_identity_unscoped":
            connection.execute(sql)
    identity_columns = """recording_id, source_sha256, algorithm_id, definition_id,
        definition_version, definition_sha256, scientific_version, implementation_identity,
        implementation_build, configuration_sha256, window_policy_version"""
    connection.execute(f"CREATE UNIQUE INDEX idx_dynamic_sessions_identity_unscoped ON dynamic_analysis_sessions({identity_columns}) WHERE project_id IS NULL")
    connection.execute(f"CREATE UNIQUE INDEX idx_dynamic_sessions_identity_scoped ON dynamic_analysis_sessions(project_id, {identity_columns}) WHERE project_id IS NOT NULL")
    for table in tables:
        connection.execute(f"DROP TABLE {table}_v19_backup")

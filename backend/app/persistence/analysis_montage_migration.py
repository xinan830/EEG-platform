"""Recording-bound, immutable analysis montage definitions; no raw-file changes."""

import sqlite3


def migration_022_analysis_montage_snapshots(connection: sqlite3.Connection) -> None:
    connection.execute("""CREATE TABLE analysis_montage_snapshots (
        recording_id TEXT NOT NULL REFERENCES recordings(id) ON DELETE CASCADE,
        snapshot_id TEXT NOT NULL,
        profile_json TEXT NOT NULL,
        snapshot_sha256 TEXT NOT NULL,
        created_at TEXT NOT NULL,
        PRIMARY KEY (recording_id, snapshot_id)
    )""")

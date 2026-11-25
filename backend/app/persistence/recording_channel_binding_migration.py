"""Persist optional import-time channel display metadata."""

from __future__ import annotations

import sqlite3


def migration_023_recording_channel_bindings(connection: sqlite3.Connection) -> None:
    columns = {str(row[1]) for row in connection.execute("PRAGMA table_info(recordings)")}
    for name in ("import_channel_configuration_snapshot_json", "import_channel_column_mapping_json"):
        if name not in columns:
            connection.execute(f"ALTER TABLE recordings ADD COLUMN {name} TEXT")


def migration_024_recording_montage_bindings(connection: sqlite3.Connection) -> None:
    columns = {str(row[1]) for row in connection.execute("PRAGMA table_info(recordings)")}
    for name in ("import_acquisition_montage_snapshot_json", "import_montage_configuration_snapshot_json",
                 "import_montage_catalogue_snapshot_json"):
        if name not in columns:
            connection.execute(f"ALTER TABLE recordings ADD COLUMN {name} TEXT")

"""SQLite persistence for recording sources and channel metadata."""

import json
import sqlite3
from pathlib import Path

from app.models.recording import RecordingSummary
from app.persistence import connect_database


class RecordingRepository:
    def __init__(self, database_path: Path):
        self._database_path = database_path

    def insert_source(self, recording: RecordingSummary) -> None:
        with connect_database(self._database_path) as connection:
            connection.execute(
                """INSERT INTO recordings (
                    id, original_name, stored_name, extension, created_at,
                    source_sha256, file_size_bytes, import_version
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    recording.id, recording.original_name, recording.stored_name,
                    recording.extension, recording.created_at, recording.source_sha256,
                    recording.file_size_bytes, recording.import_version,
                ),
            )

    def update_channels(self, recording_id: str, sfreq: float, duration_s: float,
                        raw_labels: list[str], canonical: list[str], channel_types: list[str],
                        channel_units: list[str], import_version: str) -> None:
        raw_labels_json = json.dumps(raw_labels, ensure_ascii=False)
        with connect_database(self._database_path) as connection:
            connection.execute(
                """UPDATE recordings SET sfreq = ?, duration_s = ?, channels_json = ?,
                   raw_channel_labels_json = ?, canonical_channel_labels_json = ?,
                   channel_types_json = ?, channel_units_json = ?, import_version = ?
                   WHERE id = ?""",
                (
                    sfreq, duration_s, raw_labels_json, raw_labels_json,
                    json.dumps(canonical, ensure_ascii=False),
                    json.dumps(channel_types, ensure_ascii=False),
                    json.dumps(channel_units, ensure_ascii=False),
                    import_version, recording_id,
                ),
            )

    @staticmethod
    def insert_with_metadata(connection: sqlite3.Connection, recording: RecordingSummary) -> None:
        """Insert the recording within the caller's publication transaction."""
        connection.execute(
            """INSERT INTO recordings (id, original_name, stored_name, extension, created_at, sfreq, duration_s,
               channels_json, source_sha256, file_size_bytes, raw_channel_labels_json, canonical_channel_labels_json,
               channel_types_json, channel_units_json, import_version,
               import_channel_configuration_snapshot_json, import_channel_column_mapping_json,
               import_acquisition_montage_snapshot_json, import_montage_configuration_snapshot_json,
               import_montage_catalogue_snapshot_json)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (
                recording.id, recording.original_name, recording.stored_name, recording.extension,
                recording.created_at, recording.sfreq, recording.duration_s,
                json.dumps(recording.channels, ensure_ascii=False), recording.source_sha256, recording.file_size_bytes,
                json.dumps(recording.raw_channel_labels, ensure_ascii=False),
                json.dumps(recording.canonical_channel_labels, ensure_ascii=False),
                json.dumps(recording.channel_types, ensure_ascii=False),
                json.dumps(recording.channel_units, ensure_ascii=False), recording.import_version,
                recording.import_channel_configuration_snapshot_json, recording.import_channel_column_mapping_json,
                recording.import_acquisition_montage_snapshot_json, recording.import_montage_configuration_snapshot_json,
                recording.import_montage_catalogue_snapshot_json,
            ),
        )

    def register_wpf_source(self, recording: RecordingSummary) -> str:
        """Reuse a registered source by path or content and rebind a stale path."""
        lookup = """SELECT id, stored_name, source_sha256, import_version FROM recordings
                    WHERE (source_sha256 = ? AND import_version = ?) OR stored_name = ?"""
        identity = (recording.source_sha256, recording.import_version, recording.stored_name)
        with connect_database(self._database_path) as connection:
            existing = connection.execute(lookup, identity).fetchone()
            if existing is not None:
                stored_name = str(existing["stored_name"])
                if (stored_name.startswith(("wpf:", "wpf-montage:"))
                        and existing["source_sha256"] == recording.source_sha256
                        and existing["import_version"] == recording.import_version
                        and not Path(stored_name.partition(":")[2]).is_dir()):
                    connection.execute(
                        "UPDATE recordings SET stored_name = ?, original_name = ? WHERE id = ?",
                        (recording.stored_name, recording.original_name, existing["id"]),
                    )
                return str(existing["id"])
            try:
                self.insert_with_metadata(connection, recording)
            except sqlite3.IntegrityError:
                # A concurrent request may win between the SELECT and INSERT.
                existing = connection.execute(lookup, identity).fetchone()
                if existing is None:
                    raise
                return str(existing["id"])
        return recording.id

    def is_referenced(self, recording_id: str) -> bool:
        with connect_database(self._database_path) as connection:
            tables = {row["name"] for row in connection.execute("SELECT name FROM sqlite_master WHERE type = 'table'")}
            for table in ("sessions", "analyses", "analysis_runs", "event_markers", "report_snapshots",
                          "dynamic_analysis_sessions", "batch_run_items"):
                if table in tables and connection.execute(
                    f"SELECT 1 FROM {table} WHERE recording_id = ? LIMIT 1", (recording_id,)
                ).fetchone() is not None:
                    return True
        return False

    def list(self) -> list[RecordingSummary]:
        with connect_database(self._database_path) as connection:
            rows = connection.execute("SELECT * FROM recordings ORDER BY created_at DESC").fetchall()
        return [self._row_to_summary(row) for row in rows]

    def get(self, recording_id: str) -> RecordingSummary | None:
        with connect_database(self._database_path) as connection:
            row = connection.execute("SELECT * FROM recordings WHERE id = ?", (recording_id,)).fetchone()
        return self._row_to_summary(row) if row is not None else None

    def delete(self, recording_id: str) -> None:
        with connect_database(self._database_path) as connection:
            connection.execute("DELETE FROM recordings WHERE id = ?", (recording_id,))

    @staticmethod
    def _row_to_summary(row: sqlite3.Row) -> RecordingSummary:
        return RecordingSummary(
            id=row["id"],
            original_name=row["original_name"],
            stored_name=row["stored_name"],
            extension=row["extension"],
            created_at=row["created_at"],
            sfreq=row["sfreq"],
            duration_s=row["duration_s"],
            channels=tuple(json.loads(row["channels_json"])),
            analysis_channels=(),
            source_sha256=row["source_sha256"],
            file_size_bytes=row["file_size_bytes"],
            raw_channel_labels=tuple(json.loads(row["raw_channel_labels_json"] or "[]")),
            canonical_channel_labels=tuple(json.loads(row["canonical_channel_labels_json"] or "[]")),
            channel_types=tuple(json.loads(row["channel_types_json"] or "[]")),
            channel_units=tuple(json.loads(row["channel_units_json"] or "[]")),
            import_version=row["import_version"],
            import_channel_configuration_snapshot_json=row["import_channel_configuration_snapshot_json"],
            import_channel_column_mapping_json=row["import_channel_column_mapping_json"],
            import_acquisition_montage_snapshot_json=row["import_acquisition_montage_snapshot_json"],
            import_montage_configuration_snapshot_json=row["import_montage_configuration_snapshot_json"],
            import_montage_catalogue_snapshot_json=row["import_montage_catalogue_snapshot_json"],
        )

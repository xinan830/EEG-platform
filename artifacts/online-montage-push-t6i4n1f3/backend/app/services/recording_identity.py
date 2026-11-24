"""Recording source and channel identity backfill helpers."""

from __future__ import annotations

import hashlib
import json
import sqlite3
from pathlib import Path

from app.persistence import connect_database


RECORDING_IMPORT_VERSION = "recording-import-v1"


def canonical_channel_label(value: str) -> str:
    return " ".join(str(value).strip().split())


def backfill_recording_identity(database_path: Path, storage_dir: Path) -> None:
    """Fill additive provenance for legacy rows without requiring source recovery."""
    connection = connect_database(database_path)
    try:
        rows = connection.execute(
            """SELECT id, stored_name, channels_json, source_sha256,
               raw_channel_labels_json, canonical_channel_labels_json,
               channel_types_json, channel_units_json
               FROM recordings"""
        ).fetchall()
        for row in rows:
            path = Path(storage_dir) / row["stored_name"]
            digest = row["source_sha256"]
            byte_size = None
            if not digest and path.is_file():
                digest = _sha256_file(path)
                byte_size = path.stat().st_size
            labels = json.loads(row["channels_json"] or "[]")
            raw_labels = json.loads(row["raw_channel_labels_json"] or "[]") or labels
            canonical = json.loads(row["canonical_channel_labels_json"] or "[]") or [
                canonical_channel_label(name) for name in raw_labels
            ]
            types = json.loads(row["channel_types_json"] or "[]") or ["unknown"] * len(raw_labels)
            units = json.loads(row["channel_units_json"] or "[]") or ["unknown"] * len(raw_labels)
            connection.execute(
                """UPDATE recordings SET source_sha256 = COALESCE(source_sha256, ?),
                   file_size_bytes = COALESCE(file_size_bytes, ?), raw_channel_labels_json = ?,
                   canonical_channel_labels_json = ?, channel_types_json = ?, channel_units_json = ?
                   WHERE id = ?""",
                (
                    digest, byte_size, json.dumps(raw_labels, ensure_ascii=False),
                    json.dumps(canonical, ensure_ascii=False), json.dumps(types, ensure_ascii=False),
                    json.dumps(units, ensure_ascii=False), row["id"],
                ),
            )
        connection.commit()
    finally:
        connection.close()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

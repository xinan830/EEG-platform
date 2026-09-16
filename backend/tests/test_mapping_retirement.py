import sqlite3

from fastapi.testclient import TestClient

from app.main import app
from app.persistence.migrations import migrate_database
from app.services.recordings import RecordingService


def test_mapping_write_endpoint_is_retired(tmp_path):
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    app.state.recording_service = service

    response = TestClient(app).put(
        f"/api/recordings/{recording.id}/mapping",
        json={"fz": "Fz", "pz": "Pz", "oz": "Oz"},
    )

    assert response.status_code == 404


def test_mapping_migration_clears_active_recording_values(tmp_path):
    database = tmp_path / "catalog.sqlite3"
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "INSERT INTO recordings (id, original_name, stored_name, extension, created_at, channels_json, mapping_json) VALUES (?, ?, ?, ?, ?, ?, ?)",
            ("r1", "sample.edf", "r1.edf", ".edf", "now", "[]", '{"fz":"Fz","pz":"Pz","oz":"Oz"}'),
        )
        connection.execute("UPDATE schema_metadata SET value = '7' WHERE key = 'schema_version'")
    migrate_database(database)
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT mapping_json FROM recordings WHERE id = 'r1'").fetchone()[0] is None

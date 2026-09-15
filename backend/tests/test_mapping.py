from fastapi.testclient import TestClient

from app.main import app
from app.services.recordings import RecordingService


def test_mapping_requires_distinct_fz_pz_oz(tmp_path):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    app.state.recording_service = service

    response = TestClient(app).put(
        f"/api/recordings/{recording.id}/mapping",
        json={"fz": "C1", "pz": "C1", "oz": "C3", "f3": None, "f4": None},
    )

    assert response.status_code == 422
    assert "重复" in response.json()["detail"]


def test_import_persists_only_complete_exact_label_mapping(tmp_path, monkeypatch):
    service = RecordingService(storage_dir=tmp_path / "recordings", database_path=tmp_path / "catalog.sqlite3")
    monkeypatch.setattr(service, "_read_metadata", lambda _path: (500.0, 10.0, ["Fz", "Pz", "Oz", "F3", "F4"], [], []))

    imported = service.create_imported_recording("exact.bdf", ".bdf", b"raw")

    assert imported.mapping is not None
    assert imported.mapping.fz == "Fz"
    assert imported.mapping.pz == "Pz"
    assert imported.mapping.oz == "Oz"


def test_import_does_not_infer_o2_as_oz_mapping(tmp_path, monkeypatch):
    service = RecordingService(storage_dir=tmp_path / "recordings", database_path=tmp_path / "catalog.sqlite3")
    monkeypatch.setattr(service, "_read_metadata", lambda _path: (500.0, 10.0, ["Fz", "Pz", "O2"], [], []))

    imported = service.create_imported_recording("o2.bdf", ".bdf", b"raw")

    assert imported.mapping is None

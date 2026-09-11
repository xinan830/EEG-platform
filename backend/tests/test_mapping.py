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

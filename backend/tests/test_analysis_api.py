from fastapi.testclient import TestClient

from app.main import app
from app.services.recordings import RecordingService


def test_analysis_requires_a_saved_channel_mapping(tmp_path):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    app.state.recording_service = service

    response = TestClient(app).post(f"/api/recordings/{recording.id}/analysis")

    assert response.status_code == 409
    assert "映射" in response.json()["detail"]

from fastapi.testclient import TestClient

from app.main import app
from app.services.recordings import RecordingService
from app.services.runs import RunService


class NoopAuditService:
    def record(self, *_args, **_kwargs) -> None:
        return None


def test_legacy_analysis_creation_is_retired(tmp_path, monkeypatch):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state, "run_service", RunService(service, service.database_path, tmp_path / "artifacts"))
    monkeypatch.setattr(app.state, "audit_service", NoopAuditService())

    response = TestClient(app).post(f"/api/recordings/{recording.id}/analysis")

    assert response.status_code == 410
    assert response.json()["code"] == "LEGACY_ANALYSIS_RETIRED"



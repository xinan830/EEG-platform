from fastapi.testclient import TestClient
import json

from app.main import app
from app.services.recordings import RecordingService
from app.services.runs import RunService


class NoopAuditService:
    def record(self, *_args, **_kwargs) -> None:
        return None


def test_legacy_analysis_creation_route_is_removed(tmp_path, monkeypatch):
    service = RecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("sample.bdf", ".bdf", b"raw")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state, "run_service", RunService(service, service.database_path, tmp_path / "artifacts"))
    monkeypatch.setattr(app.state, "audit_service", NoopAuditService())

    response = TestClient(app).post(f"/api/recordings/{recording.id}/analysis")

    assert response.status_code == 404


def test_historical_legacy_analysis_remains_read_only_after_calculator_retirement(tmp_path, monkeypatch):
    service = RecordingService(tmp_path / "recordings", tmp_path / "catalog.sqlite3")
    recording = service.create_recording("historic.edf", ".edf", b"historic")
    with service._connect() as connection:
        connection.execute(
            "INSERT INTO analyses (analysis_id, recording_id, status, result_json) VALUES (?, ?, ?, ?)",
            ("historic-analysis", recording.id, "completed", json.dumps({"locked_iapf": 10.0})),
        )
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state, "run_service", RunService(service, service.database_path, tmp_path / "artifacts"))

    response = TestClient(app).get("/api/analyses/historic-analysis")

    assert response.status_code == 200
    assert response.json()["analysis_id"] == "historic-analysis"
    assert response.json()["locked_iapf"] == 10.0
    assert response.json()["algorithm_contract"]["algorithm_version"] == "legacy-unversioned"



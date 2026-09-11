from pathlib import Path

from fastapi.testclient import TestClient

from app.services.audit import AuditService


def test_audit_events_are_persistent_and_filterable(tmp_path: Path):
    path = tmp_path / "catalog.sqlite3"
    service = AuditService(path)
    service.record("recording.import", "request-1", recording_id="recording-1", parameters={"extension": ".bdf"})
    service.record("waveform.window", "request-2", recording_id="recording-2", parameters={"start_s": 10.0})
    reloaded = AuditService(path)
    events = reloaded.list_events(recording_id="recording-1")
    assert len(events) == 1
    assert events[0]["action"] == "recording.import"
    assert events[0]["parameters"] == {"extension": ".bdf"}


def test_audit_event_limit_is_bounded(tmp_path: Path):
    service = AuditService(tmp_path / "catalog.sqlite3")
    for index in range(3):
        service.record("waveform.window", f"request-{index}")
    assert len(service.list_events(limit=2)) == 2


def test_audit_api_returns_structured_parameters_without_storage_column(tmp_path: Path, monkeypatch):
    from app.main import app

    service = AuditService(tmp_path / "catalog.sqlite3")
    service.record("waveform.window", "request-1", recording_id="recording-1", parameters={"start_s": 5.0})
    monkeypatch.setattr(app.state, "audit_service", service, raising=False)
    response = TestClient(app).get("/api/audit/events", params={"recording_id": "recording-1"})
    assert response.status_code == 200
    assert response.json()[0]["parameters"] == {"start_s": 5.0}
    assert "parameters_json" not in response.json()[0]

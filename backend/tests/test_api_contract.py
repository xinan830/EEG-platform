from types import SimpleNamespace

from fastapi.testclient import TestClient

from app.main import app


def test_http_errors_have_stable_shape_and_request_id():
    response = TestClient(app).get(
        "/api/no-such-route",
        headers={"X-Request-ID": "contract-test"},
    )

    assert response.status_code == 404
    assert response.headers["X-Request-ID"] == "contract-test"
    assert response.json()["code"] == "RESOURCE_NOT_FOUND"
    assert response.json()["message"] == response.json()["detail"]
    assert response.json()["request_id"] == "contract-test"


def test_legacy_recording_event_and_playback_errors_use_specific_codes():
    client = TestClient(app)

    recording = client.get("/api/recordings/does-not-exist")
    event = client.get("/api/recordings/does-not-exist/events")
    playback = client.post(
        "/api/waveform-playback/does-not-exist/control",
        json={"action": "pause"},
    )

    assert (recording.status_code, recording.json()["code"]) == (404, "RECORDING_NOT_FOUND")
    assert (event.status_code, event.json()["code"]) == (404, "RECORDING_NOT_FOUND")
    assert (playback.status_code, playback.json()["code"]) == (404, "WAVEFORM_PLAYBACK_SESSION_NOT_FOUND")


def test_report_not_found_uses_specific_code(monkeypatch):
    class RecordingService:
        def require_recording(self, _recording_id):
            return SimpleNamespace(id="recording-1")

    class ReportService:
        def get(self, _recording_id, _report_id):
            return None

    monkeypatch.setattr(app.state, "recording_service", RecordingService())
    monkeypatch.setattr(app.state, "report_snapshot_service", ReportService())

    response = TestClient(app).get("/api/recordings/recording-1/reports/missing")

    assert response.status_code == 404
    assert response.json()["code"] == "REPORT_NOT_FOUND"


def test_unexpected_preview_failure_is_not_misreported_as_invalid_request(monkeypatch):
    class RecordingService:
        def require_recording(self, _recording_id):
            return SimpleNamespace(id="recording-1")

        def load_preview(self, *_args, **_kwargs):
            raise RuntimeError("storage unavailable")

    monkeypatch.setattr(app.state, "recording_service", RecordingService())

    response = TestClient(app, raise_server_exceptions=False).get("/api/recordings/recording-1/preview")

    assert response.status_code == 500
    assert response.json()["code"] == "INTERNAL_ERROR"

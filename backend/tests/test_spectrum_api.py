from types import SimpleNamespace

from fastapi.testclient import TestClient

from app.main import app


def test_spectrum_api_preserves_request_order_and_contract(monkeypatch):
    calls = []

    class Service:
        def require_recording(self, recording_id):
            return SimpleNamespace(id=recording_id, channels=("Fz", "Pz", "Oz"))

        def load_spectrum(self, recording, start_s, window_s, channels):
            calls.append((recording.id, start_s, window_s, channels))
            return {"recording_id": recording.id, "channels": channels, "algorithm_version": "offline-spectral-v3"}

    class Audit:
        def record(self, *args, **kwargs):
            return None

    monkeypatch.setattr(app.state, "recording_service", Service())
    monkeypatch.setattr(app.state, "audit_service", Audit())
    response = TestClient(app).get("/api/recordings/r1/spectrum?start_s=3.5&window_s=30&channels=Oz,Fz")

    assert response.status_code == 200
    assert response.json()["channels"] == ["Oz", "Fz"]
    assert calls == [("r1", 3.5, 30.0, ["Oz", "Fz"])]


def test_spectrum_api_rejects_window_shorter_than_contract():
    response = TestClient(app).get("/api/recordings/r1/spectrum?window_s=3")

    assert response.status_code == 422
    assert response.json()["code"] == "INVALID_REQUEST"

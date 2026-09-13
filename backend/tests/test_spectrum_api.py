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


def test_configured_spectrum_api_passes_validated_config(monkeypatch):
    class Service:
        def require_recording(self, recording_id):
            return SimpleNamespace(id=recording_id, channels=("F3",))

        def load_configured_spectrum(self, recording, config):
            assert config.time.start_s == 10
            assert config.time.end_s == 40
            return {"analysis_config_hash": "ABC123", "channels": config.channels, "requested_start_s": 10}

    class Audit:
        def record(self, *args, **kwargs):
            return None

    monkeypatch.setattr(app.state, "recording_service", Service())
    monkeypatch.setattr(app.state, "audit_service", Audit())
    response = TestClient(app).post("/api/recordings/r1/spectrum/configured", json={
        "mode": "static", "channels": ["F3"], "time": {"start_s": 10, "end_s": 40}
    })
    assert response.status_code == 200
    assert response.json()["analysis_config_hash"] == "ABC123"
    assert response.json()["requested_start_s"] == 10


def test_configured_spectrogram_api_passes_spectrogram_mode(monkeypatch):
    class Service:
        def require_recording(self, recording_id):
            return SimpleNamespace(id=recording_id, channels=("F3",))

        def load_configured_spectrogram(self, recording, config):
            assert config.mode == "spectrogram"
            return {
                "analysis_config_hash": "SPEC123", "channels": config.channels,
                "analysis_algorithm_version": "offline-spectral-v3",
                "spectrogram_contract_version": "spectrogram-v2",
                "power_linear": {"F3": [[1.0]]}, "power_db": {"F3": [[0.0]]},
                "matrix_shape": [1, 1], "quality": {"windows": [], "clean_windows": 1, "total_windows": 1, "bad_windows": 0},
            }

    class Audit:
        def record(self, *args, **kwargs):
            return None

    monkeypatch.setattr(app.state, "recording_service", Service())
    monkeypatch.setattr(app.state, "audit_service", Audit())
    response = TestClient(app).post("/api/recordings/r1/spectrogram/configured", json={
        "mode": "spectrogram", "channels": ["F3"], "time": {"start_s": 0, "end_s": 4}
    })
    assert response.status_code == 200
    assert response.json()["analysis_config_hash"] == "SPEC123"
    assert response.json()["analysis_algorithm_version"] == "offline-spectral-v3"
    assert response.json()["spectrogram_contract_version"] == "spectrogram-v2"
    assert response.json()["matrix_shape"] == [1, 1]
    assert response.json()["quality"]["bad_windows"] == 0

from fastapi.testclient import TestClient

from app.main import app


def test_legacy_metric_playback_routes_are_absent() -> None:
    paths = {
        route.path
        for included in app.routes
        for route in getattr(getattr(included, "original_router", None), "routes", [included])
        if hasattr(route, "path")
    }
    assert "/api/recordings/{recording_id}/playback" not in paths
    assert "/api/playback/{session_id}/control" not in paths
    assert "/api/playback/{session_id}/events" not in paths
    assert "/api/recordings/{recording_id}/waveform-playback" in paths
    assert "/api/waveform-playback/{session_id}/control" in paths
    assert "/api/waveform-playback/{session_id}/events" in paths

    client = TestClient(app)
    assert client.post("/api/recordings/unknown/playback").status_code == 404
    assert client.post("/api/playback/unknown/control", json={"action": "pause"}).status_code == 404

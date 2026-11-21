from time import monotonic, sleep
from uuid import uuid4

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.services.live_analysis_contracts import LiveAnalysisAlgorithmRegistry
from app.services.live_analysis_coordinator import LiveAnalysisCoordinator
from app.services.live_rbp_algorithm import LiveRbpAlgorithm


@pytest.fixture
def live_api(monkeypatch):
    coordinator = LiveAnalysisCoordinator(LiveAnalysisAlgorithmRegistry((LiveRbpAlgorithm(),)))
    monkeypatch.setattr(app.state, "live_analysis_coordinator", coordinator)
    session_id = uuid4().hex
    path = f"/api/live-recordings/sessions/{session_id}"
    client = TestClient(app)
    response = client.post("/api/live-recordings/sessions", json={
        "session_id": session_id, "stream_revision": "stream-v1", "schema_revision": "schema-v1",
        "sampling_rate_hz": 250, "channel_names": ["F3", "F4"], "channel_units": ["V", "V"],
        "capacity_samples": 2000,
    })
    assert response.status_code == 201
    yield client, path
    client.delete(path)
    coordinator.shutdown()


def wait_api(client, path):
    deadline = monotonic() + 5
    while monotonic() < deadline:
        response = client.get(path + "/algorithms/rbp")
        assert response.status_code == 200
        result = response.json()
        if result["revision"] > 0:
            return result
        sleep(0.001)
    raise AssertionError("live API result did not arrive")


def test_generic_binary_api_keeps_rbp_payload_and_ephemeral_lifecycle(live_api):
    client, path = live_api
    t = np.arange(1000) / 250
    values = np.column_stack((18e-6 * np.sin(2 * np.pi * 10 * t), 16e-6 * np.sin(2 * np.pi * 6 * t)))
    response = client.post(path + "/batches/binary?first_sample_counter=1000&sample_count=1000",
        content=values.astype("<f8").tobytes())
    assert response.status_code == 200
    assert response.json()["schema_version"] == "live-sample-ack-v2"
    assert response.json()["updates"] == []
    started = client.post(path + "/algorithms/rbp", json={
        "schema_version": "live-analysis-config-v1",
        "config": {"schema_version": "live-rbp-config-v1", "channels": ["F4", "F3"], "window_s": 4},
    })
    assert started.status_code == 201
    result = wait_api(client, path)
    assert result["type"] == "live_analysis_update"
    assert result["schema_version"] == "live-analysis-update-v1"
    assert result["algorithm_id"] == "rbp" and result["state"] == "ready"
    assert (result["start_sample"], result["end_sample"], result["start_s"], result["end_s"]) == (1000, 2000, 0, 4)
    assert result["payload"]["channels"] == ["F4", "F3"]
    assert result["payload"]["scientific_identity"] == "rbp-live-v1"
    assert result["payload"]["units"]["alpha"] == "ratio"
    assert client.delete(path + "/algorithms/rbp").status_code == 204
    assert client.get(path + "/algorithms/rbp").json()["code"] == "LIVE_ALGORITHM_NOT_STARTED"
    assert client.delete(path).status_code == 204
    stale = client.post(path + "/batches/binary?first_sample_counter=2000&sample_count=1", content=values[:1].tobytes())
    assert stale.status_code == 422 and stale.json()["code"] == "LIVE_SESSION_NOT_FOUND"


@pytest.mark.parametrize("algorithm,config,code", [
    ("unknown", {}, "LIVE_ALGORITHM_UNSUPPORTED"),
    ("rbp", {"channels": ["F3", "f3"]}, "LIVE_CONFIG_INVALID"),
    ("rbp", {"channels": ["missing"]}, "LIVE_CHANNEL_NOT_FOUND"),
    ("rbp", {"channels": ["F3"], "window_s": 60}, "LIVE_WINDOW_EXCEEDS_RETENTION"),
])
def test_generic_start_failures_are_typed_and_do_not_break_samples(live_api, algorithm, config, code):
    client, path = live_api
    response = client.post(path + "/algorithms/" + algorithm, json={"config": config})
    assert response.status_code == 422 and response.json()["code"] == code
    assert response.json()["request_id"]
    assert client.get(path + "/range").status_code == 200


def test_generic_commands_reject_wrong_schema_and_closed_sessions(live_api):
    client, path = live_api
    assert client.post(path + "/algorithms/rbp", json={"schema_version": "wrong", "config": {}}).status_code == 422
    client.delete(path)
    assert client.post(path + "/algorithms/rbp", json={"config": {"channels": ["F3"]}}).json()["code"] == "LIVE_SESSION_NOT_FOUND"
    assert client.delete(path + "/algorithms/rbp").json()["code"] == "LIVE_SESSION_NOT_FOUND"

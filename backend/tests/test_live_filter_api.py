import numpy as np
from fastapi.testclient import TestClient

from app.main import app


def test_live_filter_contract_is_available_for_desktop_cache_identity() -> None:
    response = TestClient(app).get("/api/live-filters/contract")

    assert response.status_code == 200
    assert response.json()["unit"] == "V"
    assert response.json()["filter_contract"]["algorithm_version"] == "display-iir-sos-v2"
    assert response.json()["filter_contract"]["phase"] == "causal"


def test_binary_live_filter_batch_uses_float64_without_json_number_roundtrip() -> None:
    client = TestClient(app)
    session_id = "binary-live-filter-test"
    created = client.post(
        "/api/live-filters/sessions",
        json={
            "session_id": session_id,
            "sampling_rate_hz": 4000,
            "channel_count": 3,
            "eeg_channel_indexes": [0, 1],
            "low_cut_hz": 1.0,
            "high_cut_hz": 100.0,
            "notch_hz": None,
        },
    )
    assert created.status_code == 201

    source = np.column_stack(
        (
            np.sin(np.arange(200) / 5) * 1e-6,
            np.cos(np.arange(200) / 7) * 1e-6,
            np.arange(200),
        )
    ).astype("<f8")
    response = client.post(
        f"/api/live-filters/sessions/{session_id}/batches/binary?sample_count=200",
        content=source.tobytes(),
        headers={"Content-Type": "application/vnd.brain-platform.float64"},
    )

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/vnd.brain-platform.float64")
    assert response.headers["x-sample-count"] == "200"
    assert response.headers["x-unit"] == "V"
    result = np.frombuffer(response.content, dtype="<f8").reshape(200, 3)
    assert result.shape == source.shape
    np.testing.assert_array_equal(result[:, 2], source[:, 2])
    assert np.isfinite(result[:, :2]).all()

    closed = client.delete(f"/api/live-filters/sessions/{session_id}")
    assert closed.status_code == 204


def test_binary_warmup_advances_filter_state_without_returning_waveform() -> None:
    client = TestClient(app)
    session_id = "binary-live-filter-warmup-test"
    created = client.post(
        "/api/live-filters/sessions",
        json={
            "session_id": session_id,
            "sampling_rate_hz": 1000,
            "channel_count": 3,
            "eeg_channel_indexes": [0, 1],
            "low_cut_hz": 1.0,
            "high_cut_hz": 100.0,
            "notch_hz": None,
        },
    )
    assert created.status_code == 201

    source = np.zeros((100, 3), dtype="<f8")
    response = client.post(
        f"/api/live-filters/sessions/{session_id}/warmup/binary?sample_count=100",
        content=source.tobytes(),
        headers={"Content-Type": "application/vnd.brain-platform.float64"},
    )

    assert response.status_code == 204
    assert response.content == b""
    assert response.headers["x-warmup-sample-count"] == "100"
    assert response.headers["x-unit"] == "V"
    assert client.delete(f"/api/live-filters/sessions/{session_id}").status_code == 204


def test_checkpoint_roundtrip_preserves_causal_filter_output() -> None:
    client = TestClient(app)
    settings = {
        "sampling_rate_hz": 1000,
        "channel_count": 2,
        "eeg_channel_indexes": [0, 1],
        "low_cut_hz": 1.0,
        "high_cut_hz": 100.0,
        "notch_hz": None,
    }
    first = client.post("/api/live-filters/sessions", json={"session_id": "checkpoint-source", **settings})
    second = client.post("/api/live-filters/sessions", json={"session_id": "checkpoint-target", **settings})
    assert first.status_code == second.status_code == 201

    prefix = np.column_stack((np.sin(np.arange(100) / 7), np.cos(np.arange(100) / 11))).astype("<f8") * 1e-6
    suffix = np.column_stack((np.sin(np.arange(100, 150) / 7), np.cos(np.arange(100, 150) / 11))).astype("<f8") * 1e-6
    client.post(
        "/api/live-filters/sessions/checkpoint-source/batches/binary?sample_count=100",
        content=prefix.tobytes(),
    )
    checkpoint = client.get("/api/live-filters/sessions/checkpoint-source/checkpoint")
    assert checkpoint.status_code == 200
    assert checkpoint.json()["version"] == "display-filter-state-v1"
    imported = client.put(
        "/api/live-filters/sessions/checkpoint-target/checkpoint",
        json={"checkpoint_b64": checkpoint.json()["checkpoint_b64"]},
    )
    assert imported.status_code == 204

    source_next = client.post(
        "/api/live-filters/sessions/checkpoint-source/batches/binary?sample_count=50",
        content=suffix.tobytes(),
    )
    target_next = client.post(
        "/api/live-filters/sessions/checkpoint-target/batches/binary?sample_count=50",
        content=suffix.tobytes(),
    )
    assert source_next.status_code == target_next.status_code == 200
    np.testing.assert_array_equal(source_next.content, target_next.content)
    assert client.delete("/api/live-filters/sessions/checkpoint-source").status_code == 204
    assert client.delete("/api/live-filters/sessions/checkpoint-target").status_code == 204

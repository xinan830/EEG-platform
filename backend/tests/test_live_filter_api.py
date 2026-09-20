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

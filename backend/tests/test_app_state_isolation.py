"""Recording API clients must not mutate or close another application's services."""

import numpy as np
from fastapi.testclient import TestClient

from app.services.preprocessing.analysis_preprocess_cache import PreprocessedRecording
from app.services.analysis.scientific_compute_coordinator import ScientificComputeCoordinator
from test_recording_import import _client, _edf_bytes


def test_recording_clients_keep_independent_services_and_caches(tmp_path):
    first, recordings, _ = _client(tmp_path / "first")
    prepared = first.app.state.offline_scientific_preprocessing_service
    cached = PreprocessedRecording(np.ones((2, 1)), 10, ("Fz",))
    prepared.prepared_cache.put("recording", "version", cached)
    second, other_recordings, _ = _client(tmp_path / "second")
    other_prepared = second.app.state.offline_scientific_preprocessing_service

    assert first.app is not second.app
    assert prepared is not other_prepared
    assert prepared.prepared_cache is not other_prepared.prepared_cache
    assert prepared.ica_review_cache is not other_prepared.ica_review_cache
    assert prepared.prepared_cache.get("recording", "version") is cached
    assert other_prepared.prepared_cache.get("recording", "version") is None
    source = _edf_bytes(tmp_path / "source.edf")
    response = first.post("/api/recordings/import", files={"file": ("source.edf", source)})
    assert response.status_code == 201, response.text
    assert len(recordings.list_recordings()) == 1
    assert other_recordings.list_recordings() == []
    assert len(first.get("/api/recordings").json()) == 1
    assert second.get("/api/recordings").json() == []


def test_recording_client_lifespan_leaves_shared_compute_usable(tmp_path, monkeypatch):
    from app.main import app

    def unexpected_worker_start():
        raise AssertionError("production worker started")

    coordinator = ScientificComputeCoordinator()
    monkeypatch.setattr(app.state, "scientific_compute_coordinator", coordinator)
    monkeypatch.setattr(app.state.run_worker, "start", unexpected_worker_start)
    try:
        client, _, _ = _client(tmp_path)
        assert client.app is not app
        with TestClient(client.app) as nested_client:
            response = nested_client.get("/api/recordings")
            assert response.status_code == 200, response.text
        with coordinator.acquire("interactive"):
            assert coordinator.snapshot()["active"] == 1
    finally:
        coordinator.close()

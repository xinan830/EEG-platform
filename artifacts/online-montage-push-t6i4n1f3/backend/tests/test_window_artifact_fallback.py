"""Interrupted or incompatible derived results never become successful reads."""

import json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.results import router
from app.models.run import AnalysisRun, RunStatus
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository
from app.services.artifacts import ArtifactIntegrityError, ArtifactStore, WindowIndexUnavailable


@pytest.fixture
def saved(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    now = utc_now()
    repository.create(AnalysisRun(
        run_id="run", recording_id="recording", analysis_type="official_algorithm",
        status=RunStatus.QUEUED, scientific_version="test-v1", implementation_version="test",
        config={}, config_sha256="config", cache_key="cache", requested_range={"start_s": 0, "end_s": 8},
        channel_mapping={"channels": ["Fz"]}, reference={}, filters={}, window={}, quality_rules={},
        environment={}, created_at=now, updated_at=now,
    ))
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz("run", "official_algorithm", {
        "axis_frequency_hz": np.array([1.0, 2.0]), "psd": np.array([[1.0, 2.0], [3.0, 4.0]]),
    }, "V^2/Hz", window_ranges=[
        {"start_sample": 0, "end_sample": 400}, {"start_sample": 200, "end_sample": 600},
    ])
    index_path = (store.root / artifact.relative_path).with_suffix(".npz.windows.json")
    return store, artifact, index_path


def _damage(saved, defect):
    store, artifact, path = saved
    index = json.loads(path.read_text(encoding="utf-8"))
    if defect == "missing_index":
        path.unlink()
    elif defect == "partial_index":
        path.write_text('{"format":', encoding="utf-8")
    elif defect == "not_object":
        path.write_text("[]", encoding="utf-8")
    elif defect in {"stale", "incompatible"}:
        index["artifact_sha256" if defect == "stale" else "format"] = "old"
        path.write_text(json.dumps(index), encoding="utf-8")
    elif defect == "wrong_row":
        index["windows"][0]["window_index"] = 1
        path.write_text(json.dumps(index), encoding="utf-8")
    else:
        file = store.root / (index["static_path"] if defect.endswith("axes") else index["windows"][0]["path"])
        if defect.startswith("missing"):
            file.unlink()
        else:
            file.write_bytes(b"interrupted-npz")


@pytest.mark.parametrize("defect", [
    "missing_index", "partial_index", "not_object", "stale", "incompatible", "wrong_row",
    "missing_chunk", "corrupt_chunk", "missing_axes", "corrupt_axes",
])
def test_derived_artifact_damage_returns_structured_cache_miss(saved, defect):
    store, artifact, _ = saved
    canonical = (store.root / artifact.relative_path).read_bytes()
    _damage(saved, defect)

    def read(_run_id, **_cursor):
        store.read_window(artifact, 0)
        return store.read_window_axes(artifact)

    app = FastAPI()
    app.include_router(router)
    app.state.result_service = SimpleNamespace(window_preview=read)
    with TestClient(app) as client:
        response = client.get("/api/runs/run/window-preview?cursor_sample=400")
    assert response.status_code == 409
    code = response.json()["code"]
    assert code == ("ARTIFACT_INTEGRITY_FAILED" if defect.startswith("corrupt") else "RESULT_WINDOW_INDEX_UNAVAILABLE")
    assert (store.root / artifact.relative_path).read_bytes() == canonical
    np.testing.assert_array_equal(store.read_npz(artifact)["psd"], [[1, 2], [3, 4]])


@pytest.mark.parametrize("defect", ["stale", "incompatible", "partial_index", "not_object"])
def test_axis_reader_independently_validates_index(saved, defect):
    store, artifact, _ = saved
    _damage(saved, defect)
    with pytest.raises(WindowIndexUnavailable):
        store.read_window_axes(artifact)


def test_missing_canonical_artifact_fails_full_preview_verification(saved):
    store, artifact, _ = saved
    (store.root / artifact.relative_path).unlink()
    with pytest.raises(ArtifactIntegrityError):
        store.read_npz(artifact)

import hashlib
from pathlib import Path

import numpy as np
import pytest

from app.models.run import AnalysisRun, RunStatus
from app.models.run import RunCreateRequest
from app.core.provenance import sha256_json
from app.services.artifacts import ArtifactIntegrityError, ArtifactStore
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository


def _run(run_id: str = "run-1") -> AnalysisRun:
    now = utc_now()
    return AnalysisRun(
        run_id=run_id,
        recording_id="recording-1",
        analysis_type="spectrum",
        status=RunStatus.QUEUED,
        scientific_version="offline-spectral-v3",
        implementation_version="test-build",
        config={"channels": ["Oz", "Fz", "Pz"]},
        config_sha256="config-hash",
        cache_key="cache-key",
        requested_range={"start_s": 10.0, "end_s": 40.0},
        channel_mapping={"channels": ["Oz", "Fz", "Pz"]},
        reference={"mode": "original"},
        filters={"bandpass_hz": [1.0, 30.0]},
        window={"segment_s": 4.0, "overlap": 0.5},
        quality_rules={"minimum_clean_ratio": 0.75},
        environment={"python": "test"},
        created_at=now,
        updated_at=now,
    )


def test_run_repository_enforces_lifecycle_and_preserves_channel_order(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())

    running = repository.update_status("run-1", RunStatus.RUNNING)
    completed = repository.update_status(
        "run-1", RunStatus.COMPLETED,
        actual_range={"start_s": 10.0, "end_s": 40.0},
        result_summary={"status": "available"},
    )

    assert running.started_at is not None
    assert completed.channel_mapping["channels"] == ["Oz", "Fz", "Pz"]
    assert completed.completed_at is not None
    with pytest.raises(ValueError, match="invalid run transition"):
        repository.update_status("run-1", RunStatus.CANCELLED)


def test_npz_artifact_round_trip_and_corruption_detection(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz(
        "run-1", "psd", {"frequency_hz": np.array([1.0, 1.25]), "F3": np.array([2.0, 3.0])},
        unit="uV^2/Hz",
    )

    loaded = store.read_npz(artifact)
    np.testing.assert_array_equal(loaded["F3"], [2.0, 3.0])
    artifact_path = tmp_path / "artifacts" / artifact.relative_path
    assert artifact.sha256 == hashlib.sha256(artifact_path.read_bytes()).hexdigest()
    assert repository.list_artifacts("run-1")[0].shape["F3"] == [2]

    artifact_path.write_bytes(b"corrupted")
    with pytest.raises(ArtifactIntegrityError):
        store.read_npz(artifact)


def test_display_state_is_outside_scientific_run_config_and_identity():
    scientific_config = {"channels": ["Fz"], "time": {"start_s": 0, "end_s": 10}}
    request = RunCreateRequest(
        recording_id="recording-1",
        analysis_type="spectrum",
        config=scientific_config,
        display_state={"timebase_s": 5, "paper_speed_mm_s": 30, "sensitivity_uv_mm": 10},
    )

    assert request.config == scientific_config
    assert request.display_state["timebase_s"] == 5
    assert sha256_json(request.config) == sha256_json(scientific_config)

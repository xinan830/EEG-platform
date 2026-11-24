from pathlib import Path

import numpy as np

from app.models.run import AnalysisRun, RunStatus
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository
from app.services.artifacts import ArtifactStore, WindowIndexUnavailable


def _run() -> AnalysisRun:
    now = utc_now()
    return AnalysisRun(
        run_id="windowed-run", recording_id="recording-1", analysis_type="official_algorithm",
        status=RunStatus.QUEUED, scientific_version="test-v1", implementation_version="test",
        config={}, config_sha256="config", cache_key="cache", requested_range={"start_s": 0, "end_s": 8},
        channel_mapping={"channels": ["Fz"]}, reference={}, filters={}, window={}, quality_rules={},
        environment={}, created_at=now, updated_at=now,
    )


def test_windowed_artifact_reads_only_derived_chunk_and_rejects_missing_index(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz(
        "windowed-run", "official_algorithm",
        {
            "axis_frequency_hz": np.array([1.0, 2.0]),
            "psd": np.array([[1.0, 2.0], [3.0, 4.0]]),
        },
        "V^2/Hz",
        window_ranges=[
            {"start_sample": 0, "end_sample": 4000},
            {"start_sample": 2000, "end_sample": 6000},
        ],
    )

    np.testing.assert_array_equal(store.read_window(artifact, 1)["psd"], [3.0, 4.0])
    np.testing.assert_array_equal(store.read_window_axes(artifact)["axis_frequency_hz"], [1.0, 2.0])
    assert "psd" not in store.read_window_axes(artifact)

    plain = store.write_npz("windowed-run", "official_algorithm", {"psd": np.array([1.0, 2.0])})
    try:
        store.read_window(plain, 0)
    except WindowIndexUnavailable:
        pass
    else:
        raise AssertionError("legacy artifact must not claim bounded window support")

    store.prepare_window_index(plain, [
        {"start_sample": 0, "end_sample": 1},
        {"start_sample": 1, "end_sample": 2},
    ])
    np.testing.assert_array_equal(store.read_window(plain, 1)["psd"], [2.0])


def test_cancelled_windowed_artifact_removes_derived_files(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())
    repository.update_status("windowed-run", RunStatus.RUNNING)
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz(
        "windowed-run", "official_algorithm", {"psd": np.array([[1.0], [2.0]])},
        window_ranges=[{"start_sample": 0, "end_sample": 1}, {"start_sample": 1, "end_sample": 2}],
    )
    repository.request_cancel("windowed-run")
    repository.update_status("windowed-run", RunStatus.CANCELLED)
    store.discard_cancelled_write(artifact)
    assert not list((tmp_path / "artifacts").rglob("*.npz"))


def test_window_reader_rejects_out_of_range_and_corrupt_chunk(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz(
        "windowed-run", "official_algorithm", {"psd": np.array([[1.0], [2.0]])},
        window_ranges=[{"start_sample": 0, "end_sample": 1}, {"start_sample": 1, "end_sample": 2}],
    )
    try:
        store.read_window(artifact, 2)
    except IndexError:
        pass
    else:
        raise AssertionError("out-of-range window must fail")

    chunk = next((tmp_path / "artifacts" / "windowed-run").glob(f"{artifact.artifact_id}.windows/00000001.npz"))
    chunk.write_bytes(b"corrupt")
    from app.services.artifacts import ArtifactIntegrityError
    try:
        store.read_window(artifact, 1)
    except ArtifactIntegrityError:
        pass
    else:
        raise AssertionError("corrupt window chunk must fail integrity validation")


def test_window_reader_enforces_chunk_resource_budget(tmp_path: Path):
    repository = RunRepository(tmp_path / "runs.sqlite3")
    repository.create(_run())
    store = ArtifactStore(repository, tmp_path / "artifacts")
    artifact = store.write_npz(
        "windowed-run", "official_algorithm", {"psd": np.array([[1.0], [2.0]])},
        window_ranges=[{"start_sample": 0, "end_sample": 1}, {"start_sample": 1, "end_sample": 2}],
    )
    store.MAX_WINDOW_CHUNK_BYTES = 1
    from app.services.artifacts import WindowIndexUnavailable
    try:
        store.read_window(artifact, 0)
    except WindowIndexUnavailable:
        pass
    else:
        raise AssertionError("window resource budget must be enforced")

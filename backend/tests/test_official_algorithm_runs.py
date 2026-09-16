from pathlib import Path

import numpy as np
import pytest

from app.eeg_core.official_algorithms.registry import ensure_official_definitions, official_algorithm_catalog
from app.models.recording import ChannelMapping
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.runs import RunService
from app.main import app
from fastapi.testclient import TestClient


class OfficialSyntheticRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        time_s = np.arange(3000) / sfreq
        alpha = 12e-6 * np.sin(2 * np.pi * 10 * time_s)
        return np.column_stack((8e-6 * np.sin(2 * np.pi * 6 * time_s) + alpha, alpha * 1.1, alpha * 1.2)), sfreq, ["Fz", "Pz", "O2"], []


def _service(tmp_path: Path, *, mapped: bool = True) -> tuple[RunService, str]:
    recordings = OfficialSyntheticRecordingService(tmp_path / "recordings", tmp_path / "official.sqlite3")
    recording = recordings.create_recording("official.edf", ".edf", b"official-source")
    with recordings._connect() as connection:
        connection.execute("UPDATE recordings SET sfreq = 100, duration_s = 30, channels_json = '[\"Fz\", \"Pz\", \"O2\"]' WHERE id = ?", (recording.id,))
    if mapped:
        recordings.update_mapping(recording.id, ChannelMapping(fz="Fz", pz="Pz", oz="O2"))
    service = RunService(recordings, recordings.database_path, tmp_path / "artifacts")
    ensure_official_definitions(service.definition_service)
    return service, recording.id


def _request(recording_id: str, algorithm_id: str, *, dynamic: bool = False) -> RunCreateRequest:
    config = {"algorithm_id": algorithm_id, "time": {"start_s": 0, "end_s": 30}, "mode": "dynamic" if dynamic else "static"}
    config["channel"] = "O2" if algorithm_id == "theta_beta" else "Fz"
    if dynamic:
        config.update({"dynamic_window_s": 10, "refresh_step_s": 1})
    return RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config=config)


def test_official_iapf_static_run_is_traceable_and_returns_hz(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "iapf"))

    assert completed.status is RunStatus.COMPLETED
    assert completed.definition_id
    metric = completed.result_summary["metric"]
    assert metric["output"]["unit"] == "Hz"
    assert metric["output"]["value"] == pytest.approx(10.0, abs=0.5)
    assert metric["official"]["source"] in {"peak", "cog"}
    assert metric["source_quality"] == {
        "clean_segments": 14,
        "total_segments": 14,
        "clean_ratio": pytest.approx(1.0),
        "gate_failed": None,
        "rejected_reasons": [],
    }
    assert service.list_artifacts(completed.run_id)


def test_official_theta_beta_uses_one_selected_raw_channel(tmp_path: Path):
    service, recording_id = _service(tmp_path, mapped=False)
    completed = service.create(_request(recording_id, "theta_beta"))

    assert completed.status is RunStatus.COMPLETED
    metric = completed.result_summary["metric"]
    assert metric["channel"] == "O2"
    assert metric["output"]["unit"] == "dimensionless"
    assert metric["output"]["value"] is not None


def test_official_theta_beta_rejects_unknown_raw_channel(tmp_path: Path):
    service, recording_id = _service(tmp_path, mapped=False)
    request = _request(recording_id, "theta_beta")
    request.config["channel"] = "Oz"
    with pytest.raises(ValueError, match="channel does not exist"):
        service.create(request)


def test_official_dynamic_iapf_uses_existing_trailing_window_contract(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "iapf", dynamic=True))

    assert completed.status is RunStatus.COMPLETED
    series = completed.result_summary["metric"]["series"]
    assert len(series) == 21
    assert series[0]["window_start_s"] == 0.0
    assert series[0]["window_end_s"] == 10.0
    assert series[-1]["window_start_s"] == 20.0
    assert series[-1]["window_end_s"] == 30.0
    assert all("value" in point and "quality" in point for point in series)
    assert all(point["value"] == point["output"]["value"] for point in series)


def test_catalog_marks_only_iapf_and_theta_beta_runnable_after_cutover(tmp_path: Path):
    service, _recording_id = _service(tmp_path)
    catalog = {item.algorithm_id: item for item in official_algorithm_catalog(service.definition_service)}

    assert catalog["iapf"].availability == "available" and catalog["iapf"].is_runnable is True
    assert catalog["theta_beta"].availability == "available" and catalog["theta_beta"].is_runnable is True
    assert catalog["faa"].availability == "shadow_validation" and catalog["faa"].is_runnable is False

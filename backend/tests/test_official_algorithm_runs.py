from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

from app.algorithms.catalog import ensure_official_definitions, official_algorithm_catalog
from app.core.provenance import sha256_json
from app.models.run import RunCreateRequest, RunStatus
from app.services.recordings import RecordingService
from app.services.runs import RunService
from app.services.analysis_provenance import serialize_analysis_run
from app.services.run_analysis_executor import _RecordingAlgorithmContext
from app.main import app
from fastapi.testclient import TestClient


class OfficialSyntheticRecordingService(RecordingService):
    def load_data(self, _recording):
        sfreq = 100.0
        time_s = np.arange(3000) / sfreq
        alpha = 12e-6 * np.sin(2 * np.pi * 10 * time_s)
        return np.column_stack((
            8e-6 * np.sin(2 * np.pi * 6 * time_s) + alpha, alpha * 1.1, alpha * 1.2,
            alpha * 0.8, alpha * 1.6,
        )), sfreq, ["Fz", "Pz", "O2", "F3", "F4"], []


def _service(tmp_path: Path) -> tuple[RunService, str]:
    recordings = OfficialSyntheticRecordingService(tmp_path / "recordings", tmp_path / "official.sqlite3")
    recording = recordings.create_recording("official.edf", ".edf", b"official-source")
    with recordings._connect() as connection:
        connection.execute("UPDATE recordings SET sfreq = 100, duration_s = 30, channels_json = '[\"Fz\", \"Pz\", \"O2\", \"F3\", \"F4\"]' WHERE id = ?", (recording.id,))
    service = RunService(recordings, recordings.database_path, tmp_path / "artifacts")
    ensure_official_definitions(service.definition_service)
    return service, recording.id


def _request(recording_id: str, algorithm_id: str, *, dynamic: bool = False) -> RunCreateRequest:
    config = {"algorithm_id": algorithm_id, "time": {"start_s": 0, "end_s": 30}, "mode": "dynamic" if dynamic else "static"}
    config["channel"] = "O2" if algorithm_id == "theta_beta" else "F3" if algorithm_id == "faa" else "Fz"
    if algorithm_id == "faa":
        config["f4_channel"] = "F4"
    if dynamic:
        config.update({"dynamic_window_s": 10, "refresh_step_s": 1})
    return RunCreateRequest(recording_id=recording_id, analysis_type="official_algorithm", config=config)


def test_official_iapf_static_run_is_traceable_and_returns_hz(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "iapf"))

    assert completed.status is RunStatus.COMPLETED
    assert completed.definition_id is not None
    assert completed.definition_version == "1.0.0"
    assert completed.scientific_version == "official-iapf-v2"
    metric = completed.result_summary["metric"]
    assert metric["output"]["unit"] == "Hz"
    assert metric["output"]["value"] == pytest.approx(10.0, abs=0.5)
    assert metric["official"]["iapf_evidence"]["source"] in {"peak", "cog"}
    assert metric["source_quality"] == {
        "clean_segments": 14,
        "total_segments": 14,
        "clean_ratio": pytest.approx(1.0),
        "gate_failed": None,
        "rejected_reasons": [],
    }
    assert service.list_artifacts(completed.run_id)


def test_official_theta_beta_uses_one_selected_raw_channel(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "theta_beta"))

    assert completed.status is RunStatus.COMPLETED
    metric = completed.result_summary["metric"]
    assert metric["channel"] == "O2"
    assert metric["output"]["unit"] == "dimensionless"
    assert metric["output"]["value"] is not None


def test_official_theta_beta_rejects_unknown_raw_channel(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    request = _request(recording_id, "theta_beta")
    request.config["channel"] = "Oz"
    with pytest.raises(ValueError, match="channel does not exist"):
        service.create(request)


def test_official_dynamic_iapf_uses_the_shared_dynamic_window_contract(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "iapf", dynamic=True))

    assert completed.status is RunStatus.COMPLETED
    series = completed.result_summary["metric"]["series"]
    assert series[0]["window_start_s"] == 0.0
    assert series[0]["window_end_s"] == 4.0
    assert series[0]["time_s"] == 4.0
    assert series[0]["warmup"] is True
    assert series[0]["analysis_state"] == "Partial"
    assert series[6]["window_start_s"] == 0.0
    assert series[6]["window_end_s"] == 10.0
    assert series[6]["warmup"] is False
    assert series[6]["analysis_state"] == "Complete"
    assert all("value" in point and "quality" in point for point in series)
    assert all(point["value"] == point["output"]["value"] for point in series)
    provenance = serialize_analysis_run(completed)["analysis_provenance"]
    assert provenance["sfreq_hz"] == 100.0
    assert provenance["welch"] == {"segment_s": 4.0, "window": "hann", "overlap_fraction": 0.5, "step_s": 2.0}
    assert provenance["frequency"] == {"low_hz": 1.0, "high_hz": 30.0, "point_count": 117}
    assert provenance["quality"] == {"clean_segments": 4, "total_segments": 4, "clean_ratio": 1.0, "gate_failed": None, "rejected_reasons": []}


def test_official_runs_do_not_reuse_results_from_the_pre_evidence_contract(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    recording = service.recordings.require_recording(recording_id)
    resolved = service._resolve_request(_request(recording_id, "iapf", dynamic=True), recording)
    manifest = service.algorithm_runtime_registry.get("iapf").manifest
    legacy_definition = {
        "kind": "official_algorithm",
        "algorithm_id": manifest.algorithm_id,
        "scientific_version": manifest.scientific_version,
        "implementation_identity": manifest.implementation_identity,
    }

    assert resolved["definition_sha256"] != sha256_json(legacy_definition)


def test_catalog_marks_rbp_and_faa_runnable_but_keeps_brainbeat_shadow_only(tmp_path: Path):
    service, _recording_id = _service(tmp_path)
    catalog = {item.algorithm_id: item for item in official_algorithm_catalog(service.definition_service)}

    assert catalog["iapf"].availability == "available" and catalog["iapf"].is_runnable is True
    assert catalog["theta_beta"].availability == "available" and catalog["theta_beta"].is_runnable is True
    assert catalog["theta_beta"].required_channel_roles == []
    assert catalog["rbp"].availability == "available" and catalog["rbp"].is_runnable is True
    assert catalog["faa"].availability == "available" and catalog["faa"].is_runnable is True
    assert catalog["brainbeat"].availability == "shadow_validation" and catalog["brainbeat"].is_runnable is False
    assert catalog["iapf"].definition_id
    assert catalog["iapf"].definition_version == "1.0.0"
    assert catalog["iapf"].output_schema["fields"][0]["unit"] == "Hz"


def test_official_rbp_run_returns_all_four_backend_band_shares(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "rbp"))

    metric = completed.result_summary["metric"]
    assert completed.status is RunStatus.COMPLETED
    assert metric["output"]["value"] is None
    assert set(metric["band_values"]) == {"delta", "theta", "alpha", "beta"}
    assert sum(metric["band_values"].values()) == pytest.approx(1.0)
    assert metric["chart"]["kind"] == "band_share"


def test_official_psd_run_returns_structured_frequency_artifact(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    request = _request(recording_id, "iapf")
    request.config = {
        "algorithm_id": "psd", "time": {"start_s": 0, "end_s": 30},
        "mode": "static", "channel": "Fz",
    }

    completed = service.create(request)

    assert completed.status is RunStatus.COMPLETED
    structured = completed.result_summary["structured"]
    assert structured["output"]["kind"] == "frequency_series"
    assert structured["channel_order"] == ["Fz"]
    assert structured["axes"]["frequency_hz"]["unit"] == "Hz"
    assert structured["arrays"]["psd"]["unit"] == "V^2/Hz"
    assert service.list_artifacts(completed.run_id)


def test_official_stft_run_returns_structured_time_frequency_artifact(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    request = _request(recording_id, "stft")
    completed = service.create(request)

    assert completed.status is RunStatus.COMPLETED
    structured = completed.result_summary["structured"]
    assert structured["output"] == {
        "id": "stft", "label": "时频分析", "kind": "time_frequency",
        "quality": {"status": "clean", "reasons": []},
    }
    assert structured["channel_order"] == ["Fz"]
    assert structured["axes"]["time_center_s"]["unit"] == "s"
    assert structured["axes"]["frequency_hz"]["unit"] == "Hz"
    assert structured["arrays"]["power_linear"]["unit"] == "V^2/Hz"
    assert structured["arrays"]["power_db"]["unit"] == "dB re 1 uV^2/Hz"
    assert service.list_artifacts(completed.run_id)


def test_official_stft_dynamic_run_returns_window_time_frequency_matrix_artifact(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    request = _request(recording_id, "stft", dynamic=True)
    completed = service.create(request)

    assert completed.status is RunStatus.COMPLETED
    structured = completed.result_summary["structured"]
    assert structured["output"]["mode"] == "dynamic"
    assert structured["output"]["kind"] == "time_frequency"
    assert structured["arrays"]["power_linear"]["shape"] == [27, 7, 117]
    assert structured["arrays"]["power_db"]["unit"] == "dB re 1 uV^2/Hz"
    assert structured["window_state_counts"] == {"Partial": 6, "Complete": 21}
    assert service.list_artifacts(completed.run_id)


def test_official_psd_dynamic_run_returns_window_frequency_matrix_artifact(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "psd", dynamic=True))

    assert completed.status is RunStatus.COMPLETED
    structured = completed.result_summary["structured"]
    assert structured["output"]["mode"] == "dynamic"
    assert structured["output"]["kind"] == "frequency_series"
    assert structured["arrays"]["psd"]["shape"] == [27, 117]
    assert structured["window_state_counts"] == {"Partial": 6, "Complete": 21}
    assert service.list_artifacts(completed.run_id)


@pytest.mark.parametrize("algorithm_id", ["psd", "stft"])
def test_one_window_dynamic_result_matches_static_result(tmp_path: Path, algorithm_id: str):
    service, recording_id = _service(tmp_path)
    static_request = _request(recording_id, algorithm_id)
    dynamic_request = _request(recording_id, algorithm_id, dynamic=True)
    static_request.config["time"] = {"start_s": 20, "end_s": 30}
    dynamic_request.config.update({"time": {"start_s": 20, "end_s": 30}, "dynamic_window_s": 10, "refresh_step_s": 1})

    static_run = service.create(static_request)
    dynamic_run = service.create(dynamic_request)
    assert static_run.status is RunStatus.COMPLETED
    assert dynamic_run.status is RunStatus.COMPLETED

    static_artifact = service.list_artifacts(static_run.run_id)[0]
    dynamic_artifact = service.list_artifacts(dynamic_run.run_id)[0]
    static_arrays = service.artifacts.read_npz(static_artifact)
    dynamic_arrays = service.artifacts.read_npz(dynamic_artifact)
    if algorithm_id == "psd":
        np.testing.assert_allclose(dynamic_arrays["psd"][0], static_arrays["psd"], rtol=0, atol=1e-24)
    else:
        np.testing.assert_allclose(dynamic_arrays["power_linear"][0], static_arrays["power_linear"], rtol=0, atol=1e-24)
        np.testing.assert_allclose(dynamic_arrays["power_db"][0], static_arrays["power_db"], rtol=0, atol=1e-12)


def test_official_psd_box_range_uses_the_same_static_runtime_contract(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    request = _request(recording_id, "psd")
    request.config["time"] = {"start_s": 5, "end_s": 15}
    completed = service.create(request)

    assert completed.status is RunStatus.COMPLETED
    structured = completed.result_summary["structured"]
    assert structured["requested_range"] == {"start_s": 5.0, "end_s": 15.0}
    assert structured["actual_range"] == {"start_s": 5.0, "end_s": 15.0}
    assert structured["output"]["kind"] == "frequency_series"


def test_official_faa_run_records_explicit_pair_and_paired_quality(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    completed = service.create(_request(recording_id, "faa"))

    metric = completed.result_summary["metric"]
    assert completed.status is RunStatus.COMPLETED
    assert metric["channel"] == "F3/F4"
    assert metric["output"]["value"] == pytest.approx(np.log(4.0), abs=0.15)
    assert metric["official"]["faa_evidence"]["channels"] == ["F3", "F4"]
    assert metric["source_quality"]["clean_segments"] >= 10
    assert metric["official"]["faa_evidence"]["time_scope"] == "exact_requested_absolute_range"
    assert metric["official"]["faa_evidence"]["requested_range_s"] == {"start_s": 0.0, "end_s": 30.0}
    assert metric["official"]["faa_evidence"]["faa_contract"]["method"] == "paired_epoch_rfft_density"
    assert completed.filters == {"operation": "per_epoch_mean_removal", "software_bandpass": "not_applied"}
    assert completed.window["method"] == "paired_epoch_rfft_density"


def test_static_faa_loader_uses_the_exact_requested_absolute_range(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    recording = service.recordings.require_recording(recording_id)
    context = _RecordingAlgorithmContext(
        service.recordings,
        SimpleNamespace(
            id=recording.id,
            channels=recording.channels,
            sfreq=recording.sfreq,
            duration_s=recording.duration_s,
        ),
    )

    f3, f4, sfreq, evidence = context.load_faa_signals(
        start_s=2.0, end_s=5.0, f3_channel="F3", f4_channel="F4",
    )

    assert sfreq == 100.0
    assert len(f3) == len(f4) == 300
    assert evidence["time_scope"] == "exact_requested_absolute_range"
    assert evidence["requested_range_s"] == {"start_s": 2.0, "end_s": 5.0}
    assert evidence["actual_range_s"] == {"start_s": 2.0, "end_s": 5.0}


def test_official_faa_rejects_missing_or_duplicate_pair_sources(tmp_path: Path):
    service, recording_id = _service(tmp_path)
    duplicate = _request(recording_id, "faa")
    duplicate.config["f4_channel"] = "F3"

    with pytest.raises(ValueError, match="must be different"):
        service.create(duplicate)

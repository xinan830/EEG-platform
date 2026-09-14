from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.main import app
from app.models.recording import RecordingSummary
from app.services.recordings import RecordingService
from app.services.runs import RunService
from app.services.validations import ValidationService


class SyntheticRecordingService(RecordingService):
    def load_data(self, recording):
        sfreq = 100.0
        times = np.arange(10 * int(sfreq)) / sfreq
        values = np.column_stack([
            10e-6 * np.sin(2 * np.pi * 10 * times),
            6e-6 * np.sin(2 * np.pi * 6 * times),
        ])
        return values, sfreq, ["F3", "Fz"], []


def _configure_services(tmp_path: Path) -> tuple[TestClient, RecordingSummary]:
    service = SyntheticRecordingService(
        storage_dir=tmp_path / "recordings",
        database_path=tmp_path / "catalog.sqlite3",
    )
    recording = service.create_recording("synthetic.edf", ".edf", b"synthetic-source")
    with service._connect() as connection:
        connection.execute(
            """UPDATE recordings SET sfreq = 100, duration_s = 10, channels_json = '[\"F3\", \"Fz\"]',
               raw_channel_labels_json = '[\"F3\", \"Fz\"]', canonical_channel_labels_json = '[\"F3\", \"Fz\"]',
               channel_types_json = '[\"eeg\", \"eeg\"]', channel_units_json = '[\"V\", \"V\"]' WHERE id = ?""",
            (recording.id,),
        )
    recording = service.require_recording(recording.id)
    app.state.recording_service = service
    app.state.run_service = RunService(service, service.database_path, tmp_path / "artifacts")
    app.state.validation_service = ValidationService(service.database_path)
    return TestClient(app), recording


def test_spectrum_run_records_provenance_artifact_and_cache_reuse(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    request = {
        "recording_id": recording.id,
        "analysis_type": "spectrum",
        "config": {
            "mode": "static",
            "channels": ["Fz", "F3"],
            "time": {"start_s": 0.0, "end_s": 10.0},
        },
    }

    first = client.post("/api/runs", json=request)
    second = client.post("/api/runs", json=request)

    assert first.status_code == 201
    assert first.json()["status"] == "completed"
    assert first.json()["channel_mapping"]["channels"] == ["Fz", "F3"]
    assert first.json()["result_summary"]["artifact_channel_order"] == ["Fz", "F3"]
    assert len(first.json()["result_summary"]["artifacts"]) == 1
    assert second.json()["cache_key"] == first.json()["cache_key"]
    assert second.json()["reused_from_run_id"] == first.json()["run_id"]
    artifacts = client.get(f"/api/runs/{second.json()['run_id']}/artifacts").json()
    assert artifacts[0]["unit"] == "uV^2/Hz"


def test_run_errors_and_terminal_cancellation_use_stable_codes(tmp_path: Path):
    client, recording = _configure_services(tmp_path)

    missing = client.get("/api/runs/does-not-exist", headers={"X-Request-ID": "run-missing"})
    created = client.post("/api/runs", json={
        "recording_id": recording.id,
        "analysis_type": "spectrum",
        "config": {"channels": ["F3"], "time": {"start_s": 0, "end_s": 4}},
    })
    conflict = client.post(f"/api/runs/{created.json()['run_id']}/cancel")

    assert missing.status_code == 404
    assert missing.json()["code"] == "RUN_NOT_FOUND"
    assert missing.json()["request_id"] == "run-missing"
    assert conflict.status_code == 409
    assert conflict.json()["code"] == "RUN_NOT_CANCELLABLE"


def test_validation_api_persists_pointwise_evidence_and_report_scope(tmp_path: Path):
    client, _recording = _configure_services(tmp_path)
    response = client.post("/api/validations", json={
        "kind": "spectrogram_static_psd_parity",
        "algorithm_version": "offline-spectral-v3",
        "dataset_identity": {"kind": "synthetic"},
        "config_sha256": "ABC123",
        "tolerances": {"rtol": 1e-7, "atol": 1e-9},
        "expected": [1.0, 2.0, 3.0],
        "actual": [1.0, 2.0 + 1e-10, 3.0],
    })

    assert response.status_code == 201
    body = response.json()
    assert body["passed"] is True
    assert body["passed_point_count"] == 3
    report = client.get(f"/api/validations/{body['validation_id']}/report").json()
    assert report["report_schema_version"] == "engineering-validation-report-v1"
    assert report["scope"] == "engineering_validation_only_not_clinical_validation"
    assert "not evidence of clinical validity" in report["interpretation"]


def test_gate_failed_run_uses_null_outputs_and_structured_quality_reasons(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    service = app.state.recording_service
    service.load_data = lambda _recording: (np.ones((1000, 2)) * 2e-6, 100.0, ["F3", "Fz"], [])

    response = client.post("/api/runs", json={
        "recording_id": recording.id,
        "analysis_type": "spectrum",
        "config": {"channels": ["F3"], "time": {"start_s": 0, "end_s": 10}},
    })

    assert response.status_code == 201
    body = response.json()
    assert body["status"] == "gate_failed"
    assert body["result_summary"]["psd"] is None
    assert "flatline" in body["result_summary"]["quality"]["rejected_reasons"]
    assert body["error"]["code"] == "QUALITY_GATE_FAILED"


def test_unknown_validation_uses_stable_error_code(tmp_path: Path):
    client, _recording = _configure_services(tmp_path)
    response = client.get("/api/validations/missing")

    assert response.status_code == 404
    assert response.json()["code"] == "VALIDATION_NOT_FOUND"

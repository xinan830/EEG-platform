from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.main import app
from app.models.recording import RecordingSummary
from app.models.run import AnalysisRun, RunStatus
from app.persistence.clock import utc_now
from app.persistence.repositories.run import RunRepository
from app.services.recordings import RecordingService
from app.services.definitions import DefinitionService
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
    app.state.definition_service = DefinitionService(tmp_path / "definitions.sqlite3")
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

    assert first.status_code == 202
    assert first.json()["status"] == "completed"
    assert first.json()["channel_mapping"]["channels"] == ["Fz", "F3"]
    assert first.json()["result_summary"]["artifact_channel_order"] == ["Fz", "F3"]
    assert len(first.json()["result_summary"]["artifacts"]) == 1
    assert second.json()["cache_key"] == first.json()["cache_key"]
    assert second.json()["reused_from_run_id"] == first.json()["run_id"]
    artifacts = client.get(f"/api/runs/{second.json()['run_id']}/artifacts").json()
    assert artifacts[0]["unit"] == "uV^2/Hz"


def test_run_resource_adds_backend_authored_analysis_provenance(tmp_path: Path):
    client, recording = _configure_services(tmp_path)

    response = client.post("/api/runs", json={
        "recording_id": recording.id,
        "analysis_type": "spectrum",
        "config": {"mode": "static", "channels": ["F3"], "time": {"start_s": 0.0, "end_s": 10.0}},
    })

    assert response.status_code == 202
    body = client.get(f"/api/runs/{response.json()['run_id']}").json()
    assert body["analysis_provenance"]["contract_version"] == "analysis-provenance-v1"
    assert body["analysis_provenance"]["config_sha256"] == body["config_sha256"]
    assert body["analysis_provenance"]["welch"]["step_s"] == 2.0


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

    assert response.status_code == 202
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


def test_definition_preview_run_is_traceable_and_never_overwrites_formal_results(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    draft = {
        "semver": "0.1.0",
        "graph": {"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "outputs": ["out"]},
        "parameter_schema": {"type": "object", "additionalProperties": False},
    }
    response = client.post("/api/algorithm-definitions/preview-run", json={
        "recording_id": recording.id,
        "time": {"start_s": 1.0, "end_s": 2.0},
        "draft": draft,
        "inputs": {"value": {"value": 2.5, "unit": "ratio"}},
    })

    assert response.status_code == 201
    body = response.json()
    assert body["analysis_type"] == "definition_preview"
    assert body["is_preview"] is True
    assert body["result_summary"]["preview"] is True
    assert body["result_summary"]["outputs"]["out"]["value"] == 2.5
    assert body["result_summary"]["outputs"]["out"]["unit"] == "ratio"
    assert client.get(f"/api/runs/{body['run_id']}/artifacts").json() == []


def test_definition_preview_rejects_invalid_unit_without_creating_zero_output(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    response = client.post("/api/algorithm-definitions/preview-run", json={
        "recording_id": recording.id,
        "time": {"start_s": 1.0, "end_s": 2.0},
        "draft": {"semver": "0.1.0", "graph": {"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.value"}}], "outputs": ["out"]}},
        "inputs": {"value": {"value": 1.0, "unit": "not-a-unit"}},
    })

    assert response.status_code == 422
    assert response.json()["code"] == "INVALID_REQUEST"


def test_definition_preview_persists_unavailable_output_as_gate_failed_not_zero(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    response = client.post("/api/algorithm-definitions/preview-run", json={
        "recording_id": recording.id,
        "time": {"start_s": 1.0, "end_s": 2.0},
        "draft": {
            "semver": "0.1.0",
            "graph": {"nodes": [{"id": "out", "type": "divide", "inputs": {"left": "$input.one", "right": "$input.zero"}}], "outputs": ["out"]},
        },
        "inputs": {"one": {"value": 1.0, "unit": "ratio"}, "zero": {"value": 0.0, "unit": "ratio"}},
    })

    assert response.status_code == 201
    body = response.json()
    assert body["status"] == "gate_failed"
    assert body["result_summary"]["outputs"]["out"]["value"] is None
    assert body["error"]["code"] == "PREVIEW_OUTPUT_UNAVAILABLE"
def test_user_defined_algorithm_run_creation_returns_retired_response():
    response = TestClient(app).post("/api/runs", json={
        "recording_id": "historical-or-new",
        "analysis_type": "definition_metric",
        "definition_id": "user-definition",
        "definition_version": "1.0.0",
        "config": {"channel": "F3", "time": {"start_s": 0, "end_s": 10}},
    })
    assert response.status_code == 410
    body = response.json()
    assert body["code"] == "USER_DEFINED_ALGORITHM_RETIRED"
    assert body["message"] == "用户自定义算法已经退役，历史运行仍可读取"


def test_historical_retired_user_run_and_artifact_remain_readable(tmp_path: Path):
    client, recording = _configure_services(tmp_path)
    now = utc_now()
    run = AnalysisRun(
        run_id="historic-user-run",
        recording_id=recording.id,
        analysis_type="definition_metric",
        status=RunStatus.COMPLETED,
        definition_id="historic-user-definition",
        definition_version="1.0.0",
        scientific_version="user-definition-v1",
        implementation_version="legacy-user-definition-build",
        config={"channel": "F3", "time": {"start_s": 0.0, "end_s": 10.0}},
        config_sha256="historic-config",
        cache_key="historic-cache",
        requested_range={"start_s": 0.0, "end_s": 10.0},
        actual_range={"start_s": 0.0, "end_s": 10.0},
        channel_mapping={"channels": ["F3"]},
        reference={},
        filters={},
        window={},
        quality_rules={},
        environment={"execution_path": "legacy-user-definition"},
        result_summary={"output": {"value": 0.5, "unit": "ratio"}},
        created_at=now,
        updated_at=now,
        started_at=now,
        completed_at=now,
    )
    repository = RunRepository(app.state.run_service.repository.database_path)
    repository.create(run)
    artifact = app.state.run_service.artifacts.write_npz(
        run.run_id,
        run.analysis_type,
        {"F3": np.asarray([0.5], dtype=np.float64)},
        "ratio",
    )

    response = client.get(f"/api/runs/{run.run_id}")
    artifacts = client.get(f"/api/runs/{run.run_id}/artifacts")

    assert response.status_code == 200
    assert response.json()["definition_id"] == "historic-user-definition"
    assert response.json()["result_summary"]["output"]["value"] == 0.5
    assert artifacts.status_code == 200
    assert artifacts.json()[0]["artifact_id"] == artifact.artifact_id

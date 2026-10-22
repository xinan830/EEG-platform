"""Current-only persisted results: API, previews, provenance and export."""

import copy
import hashlib
import io
import json
import zipfile

from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from pydantic import ValidationError

from app.api.runs import router as runs_router
from app.api.results import router as results_router
from app.models.current_algorithm_result import CurrentAlgorithmResultSummary
from app.models.run_read import AnalysisRunRead
from app.services.current_algorithm_result import InvalidCurrentResultError, invalid_current_result_handler
from app.services.run_queue import PersistentRunQueue
from app.services.results import ResultService
from app.services.validations import ValidationService
from result_boundary_support import ALGORITHM_IDS
from test_official_algorithm_runs import _request, _service


def current_summary():
    return {
        "result_contract_version": "official-algorithm-result-v1",
        "metric": {
            "mode": "dynamic", "result_contract_version": "dynamic-analysis-frame-v6",
            "output": {"value": None, "unit": "Hz"},
            "actual_range": {"start_s": 0, "end_s": 30},
            "series": [{
                "result_contract_version": "dynamic-analysis-frame-v6",
                "time_s": 5, "window_start_s": 0, "window_end_s": 5,
                "analysis_state": "Unavailable", "quality": {"status": "unavailable"},
                "output": {"value": None, "unit": "Hz"},
            }],
        },
    }


@pytest.mark.parametrize("mutation", [
    lambda value: value.pop("result_contract_version"),
    lambda value: value.update(result_contract_version="retired"),
    lambda value: value["metric"].update(result_contract_version="dynamic-analysis-frame-v5"),
    lambda value: value["metric"].update(value=0),
    lambda value: value["metric"].pop("mode"),
    lambda value: value["metric"]["series"][0].pop("analysis_state"),
    lambda value: value["metric"]["series"][0].update(warmup=True),
    lambda value: value["metric"]["series"][0].pop("window_start_s"),
    lambda value: value["metric"]["series"][0].update(time_s=6),
    lambda value: value["metric"]["series"][0]["output"].pop("unit"),
    lambda value: value["metric"]["series"][0]["output"].pop("value"),
    lambda value: value["metric"]["series"][0]["output"].update(value=float("inf")),
])
def test_unsupported_or_malformed_contract_is_not_inferred_or_converted(mutation):
    summary = current_summary()
    mutation(summary)
    before = copy.deepcopy(summary)
    with pytest.raises(ValidationError):
        CurrentAlgorithmResultSummary.model_validate(summary)
    assert summary == before


def test_explicit_null_is_valid_and_public_schema_declares_current_version():
    summary = current_summary()
    parsed = CurrentAlgorithmResultSummary.model_validate(summary)
    assert parsed.metric.series[0].output.value is None
    app = FastAPI()
    app.include_router(runs_router)
    schema = app.openapi()["components"]["schemas"]
    assert schema["CurrentAlgorithmResultSummary"]["properties"]["result_contract_version"]["const"] == "official-algorithm-result-v1"
    assert schema["ScalarPoint"]["properties"]["result_contract_version"]["const"] == "dynamic-analysis-frame-v6"
    assert "result_summary" in AnalysisRunRead.model_fields


def structured_summary():
    return {
        "result_contract_version": "official-algorithm-result-v1",
        "structured": {
            "output": {"kind": "psd", "mode": "dynamic"},
            "channel_order": ["Fz"],
            "requested_range": {"start_s": 0, "end_s": 30},
            "actual_range": {"start_s": 0, "end_s": 30},
            "axes": {"frequency_hz": {"array_key": "axis_frequency_hz", "unit": "Hz", "length": 2}},
            "arrays": {"axis_frequency_hz": {"unit": "Hz", "shape": [2]},
                       "psd": {"unit": "uV^2/Hz", "shape": [1, 2]}},
            "windows": [{"state": "Complete", "quality": "good", "start_sample": 0,
                         "end_sample": 1000, "start_s": 0, "end_s": 5}],
        },
    }


@pytest.mark.parametrize("mutation", [
    lambda value: value["structured"]["output"].pop("mode"),
    lambda value: value["structured"].pop("windows"),
    lambda value: value["structured"]["windows"][0].pop("state"),
    lambda value: value["structured"]["windows"][0].pop("quality"),
    lambda value: value["structured"]["windows"][0].update(warmup=True),
    lambda value: value["structured"]["windows"][0].update(start_sample=0.5),
    lambda value: value["structured"]["axes"]["frequency_hz"].update(array_key="missing"),
    lambda value: value["structured"]["axes"]["frequency_hz"].update(length=-1),
    lambda value: value["structured"]["axes"]["frequency_hz"].update(length=3),
    lambda value: value["structured"]["arrays"]["psd"].update(shape=[-1, 2]),
    lambda value: value["structured"]["arrays"]["psd"].update(shape=[True, 2]),
    lambda value: value["structured"]["arrays"]["psd"].pop("unit"),
])
def test_structured_contract_rejects_missing_or_inconsistent_metadata(mutation):
    summary = structured_summary()
    CurrentAlgorithmResultSummary.model_validate(summary)
    mutation(summary)
    before = copy.deepcopy(summary)
    with pytest.raises(ValidationError):
        CurrentAlgorithmResultSummary.model_validate(summary)
    assert summary == before


def api_for(queue):
    app = FastAPI()
    app.include_router(runs_router)
    app.include_router(results_router)
    app.add_exception_handler(InvalidCurrentResultError, invalid_current_result_handler)
    app.state.run_service = queue
    app.state.result_service = ResultService(queue, ValidationService(queue.repository.database_path))
    return TestClient(app)


def completed_run(tmp_path, algorithm_id, dynamic):
    base, recording_id = _service(tmp_path, sfreq_hz=200)
    queue = PersistentRunQueue(base.recordings, base.repository.database_path, tmp_path / "artifacts")
    request = _request(recording_id, algorithm_id, dynamic=dynamic)
    if algorithm_id == "brainbeat":
        request.config["secondary_channel"] = "Pz"
    if algorithm_id == "band_ratio":
        request.config.update(numerator_low_hz=8, numerator_high_hz=13,
                              denominator_low_hz=13, denominator_high_hz=30)
    if algorithm_id == "faa" and dynamic:
        request.config["dynamic_window_s"] = 20
    queue.enqueue(request)
    run = queue.process_next()
    assert run.status.value == "completed", run.error
    return queue, run


def stored_bytes(queue, run):
    with queue.repository._connect() as connection:
        summary = connection.execute("SELECT result_summary_json FROM analysis_runs WHERE run_id = ?", (run.run_id,)).fetchone()[0]
    artifacts = queue.list_artifacts(run.run_id)
    hashes = {item.relative_path: hashlib.sha256((queue.base.artifacts.root / item.relative_path).read_bytes()).hexdigest()
              for item in artifacts}
    return summary, hashes


@pytest.mark.parametrize("algorithm_id", ALGORITHM_IDS)
@pytest.mark.parametrize("dynamic", [False, True], ids=["static", "dynamic"])
def test_all_nine_current_runs_retrieve_preview_export_and_provenance(tmp_path, algorithm_id, dynamic):
    queue, run = completed_run(tmp_path, algorithm_id, dynamic)
    try:
        before = stored_bytes(queue, run)
        client = api_for(queue)
        for suffix in ("", "/result"):
            response = client.get(f"/api/runs/{run.run_id}{suffix}")
            assert response.status_code == 200, response.text
            payload = response.json() if not suffix else response.json()["run"]
            assert payload["result_summary"] == run.result_summary
            assert payload["analysis_provenance"]["official_algorithm_identity"]["algorithm_id"] == algorithm_id
            assert payload["analysis_provenance"]["actual_range"] is not None
        if algorithm_id in {"psd", "stft"}:
            preview = client.get(f"/api/runs/{run.run_id}/structured-preview?max_cells=1000000")
            assert preview.status_code == 200, preview.text
            assert preview.json()["arrays"] and preview.json()["axes"]
            if dynamic:
                window = client.get(f"/api/runs/{run.run_id}/window-preview?cursor_sample=4000")
                assert window.status_code == 200, window.text
                assert window.json()["arrays"]
        else:
            metric = payload["result_summary"]["metric"]
            assert "value" not in metric
            assert metric["output"]["unit"]
            if dynamic:
                assert metric["series"]
                assert all("warmup" not in point and "value" not in point for point in metric["series"])
        exported = client.get(f"/api/runs/{run.run_id}/export")
        assert exported.status_code == 200, exported.text
        with zipfile.ZipFile(io.BytesIO(exported.content)) as archive:
            saved = json.loads(archive.read("result.json"))["run"]
            assert saved["result_summary"] == run.result_summary
            assert saved["analysis_provenance"] == payload["analysis_provenance"]
            assert any(name.endswith(".npz") for name in archive.namelist())
        assert stored_bytes(queue, run) == before
    finally:
        queue.compute_coordinator.close()


@pytest.mark.parametrize("shape", ["missing_version", "duplicate_value", "missing_state"])
def test_invalid_stored_contract_is_rejected_across_read_paths_without_writes(tmp_path, monkeypatch, shape):
    queue, run = completed_run(tmp_path, "iapf", True)
    try:
        malformed = copy.deepcopy(run.result_summary)
        if shape == "missing_version":
            malformed.pop("result_contract_version")
        elif shape == "duplicate_value":
            malformed["metric"]["value"] = 10
        else:
            malformed["metric"]["series"][0].pop("analysis_state")
        with queue.repository._connect() as connection:
            connection.execute("UPDATE analysis_runs SET result_summary_json = ? WHERE run_id = ?", (json.dumps(malformed), run.run_id))
        before = stored_bytes(queue, run)
        # Rejection must precede artifact decoding or any scientific recomputation.
        monkeypatch.setattr(queue.base.artifacts, "read_npz", lambda *args: pytest.fail("must not read arrays"))
        client = api_for(queue)
        for suffix in ("", "/result", "/structured-preview", "/window-preview?cursor_sample=4000", "/export"):
            response = client.get(f"/api/runs/{run.run_id}{suffix}")
            assert response.status_code == 409, response.text
            assert response.json()["code"] == "ALGORITHM_RESULT_INVALID"
        assert stored_bytes(queue, run) == before
    finally:
        queue.compute_coordinator.close()

import io
import hashlib
import json
import sqlite3
import time
import zipfile

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicWindowResponse
from on_demand_fixtures import configure_api, psd_config, source_hashes


def wait_window(client, window_id):
    for _ in range(300):
        response = client.get(f"/api/analysis-windows/{window_id}")
        assert response.status_code == 200, response.text
        value = response.json()
        if value["state"] not in {"queued", "preparing_source", "computing_window"}:
            return value
        time.sleep(0.01)
    pytest.fail("window did not reach a terminal state")


@pytest.mark.parametrize("gap,rejected", [(False, False), (True, False), (False, True)])
def test_real_wpf_service_api_matches_every_full_psd_window(tmp_path, gap, rejected):
    client, recording, windows, runs, root = configure_api(tmp_path, gap=gap, rejected=rejected)
    before = source_hashes(root)
    config = psd_config(recording)
    try:
        created = client.post("/api/runs", json={"recording_id": recording.id, "analysis_type": "official_algorithm", "config": config})
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        full = client.get(f"/api/runs/{run.run_id}/structured-preview").json()
        assert full["windows"]
        artifact_path = runs.base.artifacts.root / runs.list_artifacts(run.run_id)[0].relative_path
        artifact_before = artifact_path.read_bytes()
        artifact_arrays = runs.base.artifacts.read_npz(runs.list_artifacts(run.run_id)[0])
        window_evidence = json.loads(str(artifact_arrays["window_evidence_json"].item()))
        for index, frame in enumerate(full["windows"]):
            ensured = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": "psd", "config": config,
                "cursor_sample": frame["end_sample"], "request_id": f"window-{index}",
            })
            assert ensured.status_code in {200, 202}, ensured.text
            current = wait_window(client, ensured.json()["window_id"])
            DynamicWindowResponse.model_validate(current)
            assert current["range"]["start_sample"] == frame["start_sample"]
            assert current["range"]["end_sample"] == frame["end_sample"]
            expected_state = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected", "Unavailable": "unavailable"}[frame["state"]]
            assert current["state"] == expected_state, (frame, current)
            if current["state"] == "ready":
                np.testing.assert_array_equal(current["result"]["axes"]["frequency_hz"], full["axes"]["frequency_hz"])
                np.testing.assert_allclose(current["result"]["arrays"]["psd"], full["arrays"]["psd"][index], rtol=1e-12, atol=1e-24)
                np.testing.assert_allclose(current["result"]["arrays"]["band_share"], full["arrays"]["band_share"][index], rtol=1e-12, atol=1e-12)
                assert current["result"]["array_units"]["psd"] == "V^2/Hz"
                assert current["result"]["evidence"] == window_evidence[index]
            else:
                assert current["result"] is None
                assert current["failure"]["code"]
                if current["state"] == "rejected":
                    amplitude = current["failure"]["details"]["evidence"]["amplitude"]
                    assert current["failure"]["details"] == frame["failure"]["detail"]
                    if rejected:
                        assert amplitude["peak_uv"] > amplitude["threshold_uv"]
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
        assert artifact_path.read_bytes() == artifact_before
    finally:
        windows.close()
        client.close()


def test_legacy_run_api_regression_with_on_demand_enabled(tmp_path):
    client, recording, windows, runs, root = configure_api(tmp_path)
    before = source_hashes(root)
    payload = {"recording_id": recording.id, "analysis_type": "official_algorithm", "config": psd_config(recording)}
    try:
        queued = client.post("/api/runs/ensure", json=payload).json()
        assert queued["status"] == "queued"
        assert client.post("/api/runs/ensure", json=payload).json()["run_id"] == queued["run_id"]
        assert client.post(f"/api/runs/{queued['run_id']}/cancel").json()["status"] == "cancelled"
        retried = client.post(f"/api/runs/{queued['run_id']}/retry")
        assert retried.status_code == 202
        assert retried.json()["run_id"] != queued["run_id"]
        completed = runs.process_next()
        assert completed.status.value == "completed"
        assert client.post("/api/runs/ensure", json=payload).json()["run_id"] == completed.run_id
        assert client.get(f"/api/runs/{completed.run_id}/structured-preview").status_code == 200
        assert client.get(f"/api/runs/{completed.run_id}/window-preview", params={"cursor_sample": 1000}).status_code == 200
        exported = client.get(f"/api/runs/{completed.run_id}/export")
        assert exported.status_code == 200
        with zipfile.ZipFile(io.BytesIO(exported.content)) as archive:
            assert "manifest.json" in archive.namelist()
        assert source_hashes(root) == before
    finally:
        windows.close()
        client.close()


def test_full_psd_records_verified_on_demand_window_reuse(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    try:
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "psd", "config": config,
            "cursor_sample": 1000, "request_id": "reuse-window",
        })
        assert ensured.status_code in {200, 202}, ensured.text
        window = wait_window(client, ensured.json()["window_id"])
        assert window["state"] == "ready"
        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        provenance = (run.result_summary or {}).get("execution_provenance")
        assert provenance["execution_mode"] == "full"
        assert provenance["reused_on_demand_window_count"] >= 1
        assert provenance["scientific_cache_key"] == run.cache_key
        assert window["scientific_cache_key"] == run.cache_key
    finally:
        windows.close()
        client.close()


def test_full_psd_recomputes_corrupt_on_demand_window_and_records_miss(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    try:
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "psd", "config": config,
            "cursor_sample": 1000, "request_id": "corrupt-reuse-window",
        })
        assert ensured.status_code in {200, 202}, ensured.text
        window = wait_window(client, ensured.json()["window_id"])
        assert window["state"] == "ready"
        record = windows.repository.get_window(window["window_id"])
        assert record is not None and record.result_artifact_id
        (windows.artifacts_dir / f"{record.result_artifact_id}.json").write_text("{broken", encoding="utf-8")
        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        provenance = (run.result_summary or {}).get("execution_provenance")
        assert provenance["reused_on_demand_window_count"] == 0
        assert provenance["cache_miss_reasons"]["artifact_invalid"] >= 1
    finally:
        windows.close()
        client.close()


def test_full_psd_recomputes_cached_window_with_mismatched_identity(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    try:
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "psd", "config": config,
            "cursor_sample": 1000, "request_id": "identity-mismatch-window",
        })
        assert ensured.status_code in {200, 202}, ensured.text
        window = wait_window(client, ensured.json()["window_id"])
        assert window["state"] == "ready"
        record = windows.repository.get_window(window["window_id"])
        assert record is not None and record.result_artifact_id
        metadata_path = windows.artifacts_dir / f"{record.result_artifact_id}.json"
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata["identity"]["configuration_sha256"] = "tampered-identity"
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False), encoding="utf-8")
        manifest_sha256 = hashlib.sha256(metadata_path.read_bytes()).hexdigest()
        with sqlite3.connect(windows.repository.database_path) as connection:
            connection.execute(
                "UPDATE dynamic_analysis_windows SET result_manifest_sha256 = ? WHERE window_id = ?",
                (manifest_sha256, record.window_id),
            )
        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        provenance = (run.result_summary or {}).get("execution_provenance")
        assert provenance["reused_on_demand_window_count"] == 0
        assert provenance["cache_miss_reasons"]["identity_mismatch"] >= 1
    finally:
        windows.close()
        client.close()

"""Cross-mode reuse must preserve scientific output and reject invalid evidence."""

import hashlib
import io
import json
import sqlite3

import numpy as np
import pytest

from app.models.run import RunCreateRequest
from on_demand_fixtures import configure_api, psd_config
from test_on_demand_psd_api import wait_window


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize("endpoint", ["/api/runs", "/api/runs/ensure"])
def test_full_api_rejects_on_demand_even_with_synchronous_service(tmp_path, legacy, endpoint):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    if legacy:
        client.app.state.run_service = runs.base
    try:
        response = client.post(endpoint, json={
            "recording_id": recording.id, "analysis_type": "official_algorithm",
            "config": psd_config(recording), "execution_mode": "on_demand",
        })
        assert response.status_code == 422, response.text
        assert response.json()["code"] == "RUN_REQUEST_INVALID"
        assert not runs.list(recording.id)
    finally:
        windows.close()
        client.close()


@pytest.mark.parametrize("damage,reason", [
    ("missing_manifest", "manifest_unverified"),
    ("frequency_grid", "window_output_mismatch"),
    ("cache_key", "cache_key_mismatch"),
])
def test_unverified_or_scientifically_different_window_is_recomputed(tmp_path, damage, reason):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config)
    try:
        # Calculate the baseline before any on-demand artifact exists. This
        # comparison cannot pass merely by reading back a reused window.
        resolved = runs.base._resolve_request(request, recording)
        _, expected, _ = runs.base.executor.execute(request.analysis_type, recording, resolved)
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "psd", "config": config,
            "cursor_sample": 1000,
        })
        assert ensured.status_code in {200, 202}, ensured.text
        ready = wait_window(client, ensured.json()["window_id"])
        assert ready["state"] == "ready"
        row = windows.repository.get_window(ready["window_id"])
        metadata_path = windows.artifacts_dir / f"{row.result_artifact_id}.json"
        payload_path = metadata_path.with_suffix(".npz")
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        if damage == "frequency_grid":
            with np.load(payload_path, allow_pickle=False) as artifact:
                arrays = {key: artifact[key] for key in artifact.files}
            arrays["axis_frequency_hz"] = arrays["axis_frequency_hz"] + 0.01
            buffer = io.BytesIO()
            np.savez_compressed(buffer, **arrays)
            payload_path.write_bytes(buffer.getvalue())
            metadata["sha256"] = hashlib.sha256(payload_path.read_bytes()).hexdigest()
        elif damage == "cache_key":
            metadata["provenance"]["scientific_cache_key"] = "different-scientific-identity"
        metadata_path.write_text(json.dumps(metadata), encoding="utf-8")
        digest = None if damage == "missing_manifest" else hashlib.sha256(metadata_path.read_bytes()).hexdigest()
        with sqlite3.connect(windows.repository.database_path) as connection:
            connection.execute("UPDATE dynamic_analysis_windows SET result_manifest_sha256 = ? WHERE window_id = ?",
                               (digest, row.window_id))
        runs.enqueue(request)
        result = runs.process_next()
        assert result.status.value == "completed", result.error
        provenance = result.result_summary["execution_provenance"]
        assert provenance["reused_on_demand_window_count"] == 0
        assert provenance["cache_miss_reasons"][reason] == 1
        actual = runs.base.artifacts.read_npz(runs.list_artifacts(result.run_id)[0])
        for key in ("axis_frequency_hz", "psd", "band_share"):
            np.testing.assert_allclose(actual[key], expected[key], rtol=1e-12, atol=1e-24, equal_nan=True)
    finally:
        windows.close()
        client.close()


def test_cancel_during_reuse_stops_full_run_without_publishing_artifact(tmp_path, monkeypatch):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    try:
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "psd", "config": config,
            "cursor_sample": 1000,
        })
        assert wait_window(client, ensured.json()["window_id"])["state"] == "ready"
        provider = runs._offline_window_reuse.provider
        reused = []

        def cancelling_provider(**arguments):
            cached_window = provider(**arguments)

            def read(start_sample, end_sample):
                result = cached_window(start_sample, end_sample)
                if result is not None:
                    reused.append((start_sample, end_sample))
                    runs.cancel(arguments["run"].run_id)
                return result

            return read

        monkeypatch.setattr(runs._offline_window_reuse, "provider", cancelling_provider)
        runs.enqueue(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config))
        result = runs.process_next()
        assert reused
        assert result.status.value == "cancelled"
        assert not runs.list_artifacts(result.run_id)
        assert windows.get(ensured.json()["window_id"])["state"] == "ready"
    finally:
        windows.close()
        client.close()


def test_full_dynamic_run_publishes_complete_window_index_and_coverage(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    try:
        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run is not None and run.status.value == "completed", run.error
        coverage = run.result_summary["execution_provenance"]["coverage"]
        assert coverage["state"] == "complete"
        assert coverage["planned_windows"] == run.progress_total
        assert coverage["terminal_windows"] == run.progress_completed

        artifact = runs.list_artifacts(run.run_id)[0]
        index_path = runs.base.artifacts._window_index_path(artifact)
        assert index_path.is_file()
        assert len(json.loads(index_path.read_text(encoding="utf-8"))["windows"]) == coverage["planned_windows"]
        first = runs.base.artifacts.read_window(artifact, 0)
        assert {"psd", "band_share"} <= set(first)
    finally:
        windows.close()
        client.close()


def test_full_dynamic_run_cannot_complete_with_missing_window(tmp_path, monkeypatch):
    client, recording, windows, runs, _ = configure_api(tmp_path)
    config = psd_config(recording)
    original_execute = runs.base.executor.execute

    def incomplete_execute(analysis_type, source_recording, resolved):
        result, arrays, unit = original_execute(analysis_type, source_recording, resolved)
        structured = result["structured"]
        structured["windows"] = structured["windows"][:-1]
        return result, arrays, unit

    monkeypatch.setattr(runs.base.executor, "execute", incomplete_execute)
    try:
        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run is not None and run.status.value == "failed"
        assert run.error is not None
        assert run.error.code == "FULL_COVERAGE_INCOMPLETE"
        assert not runs.list_artifacts(run.run_id)
    finally:
        windows.close()
        client.close()

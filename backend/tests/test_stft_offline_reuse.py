"""STFT full/on-demand identity and matrix reuse contract tests."""

import json

import numpy as np

from on_demand_fixtures import configure_api
from test_on_demand_psd_api import wait_window


def stft_config(recording, *, channel="F3-REF"):
    return {
        "algorithm_id": "stft", "channel": channel, "mode": "dynamic",
        "time": {"start_s": 0, "end_s": recording.duration_s},
        "dynamic_window_s": 5, "refresh_step_s": 1,
        "low_hz": 1, "high_hz": 30, "notch_hz": None,
    }


def test_full_stft_reuses_complete_on_demand_window_with_relative_time_axis(tmp_path):
    client, recording, windows, runs, _ = configure_api(tmp_path, duration_s=22.35)
    config = stft_config(recording)
    try:
        ensured = client.post("/api/analysis-windows/ensure", json={
            "recording_id": recording.id, "algorithm_id": "stft", "config": config,
            "cursor_sample": round(5 * recording.sfreq), "request_id": "stft-reuse-seed",
        })
        assert ensured.status_code in {200, 202}, ensured.text
        on_demand = wait_window(client, ensured.json()["window_id"])
        assert on_demand["state"] == "ready", on_demand
        assert on_demand["result"]["provenance"]["inner_time_axis"] == "relative_window_time_center_s"
        np.testing.assert_allclose(on_demand["result"]["axes"]["time_center_s"], [2.0, 3.0])

        created = client.post("/api/runs", json={
            "recording_id": recording.id, "analysis_type": "official_algorithm", "config": config,
        })
        assert created.status_code == 202, created.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        provenance = run.result_summary["execution_provenance"]
        assert provenance["execution_mode"] == "full"
        assert provenance["reused_on_demand_window_count"] == 1

        artifact = runs.list_artifacts(run.run_id)[0]
        arrays = runs.base.artifacts.read_npz(artifact)
        structured = run.result_summary["structured"]
        matching = next(item for item in structured["windows"]
                        if item["start_s"] == on_demand["range"]["start_s"]
                        and item["end_s"] == on_demand["range"]["end_s"])
        assert matching["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
        assert matching["provenance"]["inner_time_axis"] == "relative_window_time_center_s"
        assert matching["provenance"]["analysis_state"] == "Complete"
        index = structured["windows"].index(matching)
        np.testing.assert_array_equal(arrays["axis_time_center_s"], [2.0, 3.0])
        np.testing.assert_array_equal(arrays["axis_frequency_hz"], on_demand["result"]["axes"]["frequency_hz"])
        np.testing.assert_allclose(arrays["power_linear"][index], on_demand["result"]["arrays"]["power_linear"], rtol=1e-12, atol=1e-24)
        np.testing.assert_allclose(arrays["power_db"][index], on_demand["result"]["arrays"]["power_db"], rtol=1e-12, atol=1e-12)
        assert json.loads(str(arrays["window_evidence_json"].item()))[index] == on_demand["result"]["evidence"]
    finally:
        windows.close()
        client.close()

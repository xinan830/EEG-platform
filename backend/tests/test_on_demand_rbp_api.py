import time

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicWindowResponse
from on_demand_fixtures import configure_api, source_hashes


def wait_window(client, window_id):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        response = client.get(f"/api/analysis-windows/{window_id}")
        assert response.status_code == 200, response.text
        value = response.json()
        if value["state"] not in {"queued", "preparing_source", "computing_window"}:
            return value
        time.sleep(0.01)
    pytest.fail("RBP window did not finish")


@pytest.mark.parametrize("gap,rejected", [(False, False), (True, False), (False, True)])
def test_rbp_real_file_api_matches_every_full_run_point(tmp_path, gap, rejected):
    client, recording, service, runs, root = configure_api(tmp_path, gap=gap, rejected=rejected)
    before = source_hashes(root)
    config = {"algorithm_id": "rbp", "channel": "F3-REF", "mode": "dynamic",
              "time": {"start_s": 0, "end_s": recording.duration_s}, "dynamic_window_s": 5, "refresh_step_s": 1}
    try:
        response = client.post("/api/runs", json={"recording_id": recording.id,
            "analysis_type": "official_algorithm", "config": config})
        assert response.status_code == 202, response.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        series = run.result_summary["metric"]["series"]
        assert series
        for index, frame in enumerate(series):
            ensured = client.post("/api/analysis-windows/ensure", json={"recording_id": recording.id,
                "algorithm_id": "rbp", "config": config, "cursor_sample": round(frame["window_end_s"] * recording.sfreq),
                "request_id": f"rbp-window-{index}"})
            assert ensured.status_code in {200, 202}, ensured.text
            window = wait_window(client, ensured.json()["window_id"])
            DynamicWindowResponse.model_validate(window)
            expected = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected", "Unavailable": "unavailable"}
            assert window["state"] == expected[frame["analysis_state"]], window
            assert window["range"]["start_s"] == frame["window_start_s"]
            assert window["range"]["end_s"] == frame["window_end_s"]
            if window["state"] == "ready":
                for name, value in frame["band_values"].items():
                    np.testing.assert_allclose(window["result"]["arrays"][name], [value], rtol=1e-12, atol=1e-12)
                    assert window["result"]["array_units"][name] == "ratio"
                for key in ("source_quality", "spectral_evidence", "calculation_trace"):
                    assert window["result"]["evidence"][key] == frame["official"][key]
                extensions = window["result"]["evidence"]["extensions"]
                assert set(extensions) == {"sample_coordinate"}
                for name, value in extensions.items():
                    assert value == frame["official"][name]
                assert window["result"]["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
            else:
                assert window["result"] is None
                assert window["failure"]["code"] in frame["quality"]["reasons"]
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()

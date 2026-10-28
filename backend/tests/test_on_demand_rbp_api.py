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
    config = {"algorithm_id": "rbp", "channel": "F3-REF", "channels": ["F3-REF"], "mode": "dynamic",
              "time": {"start_s": 0, "end_s": recording.duration_s}, "dynamic_window_s": 5, "refresh_step_s": 1}
    try:
        response = client.post("/api/runs", json={"recording_id": recording.id,
            "analysis_type": "official_algorithm", "config": config})
        assert response.status_code == 202, response.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        structured = run.result_summary["structured"]
        frames = structured["windows"]
        assert frames
        for index, frame in enumerate(frames):
            ensured = client.post("/api/analysis-windows/ensure", json={"recording_id": recording.id,
                "algorithm_id": "rbp", "config": config, "cursor_sample": frame["end_sample"],
                "request_id": f"rbp-window-{index}"})
            assert ensured.status_code in {200, 202}, ensured.text
            window = wait_window(client, ensured.json()["window_id"])
            DynamicWindowResponse.model_validate(window)
            expected = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected", "Unavailable": "unavailable"}
            assert window["state"] == expected[frame["state"]], window
            assert window["range"]["start_s"] == frame["start_s"]
            assert window["range"]["end_s"] == frame["end_s"]
            if window["state"] == "ready":
                for name in ("delta", "theta", "alpha", "beta", "gamma"):
                    artifact = runs.list_artifacts(run.run_id)[0]
                    artifact_array = runs.base.artifacts.read_npz(artifact)[name]
                    np.testing.assert_allclose(window["result"]["arrays"][name], [artifact_array[index, 0]], rtol=1e-12, atol=1e-12)
                    assert window["result"]["array_units"][name] == "ratio"
                assert window["result"]["evidence"]["extensions"]["channel_order"]["channels"] == ["F3-REF"]
                assert window["result"]["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
            else:
                assert window["result"] is None
                assert window["failure"] is not None
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()

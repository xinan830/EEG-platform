"""Full-run versus per-window comparison through real file/persistence/API paths."""

import time

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicWindowResponse
from on_demand_fixtures import configure_api, source_hashes


ALGORITHMS = [
    ("band_ratio", "ratio", "ratio", {
        "numerator_low_hz": 8, "numerator_high_hz": 12,
        "denominator_low_hz": 15, "denominator_high_hz": 25}),
    ("peak_frequency", "peak_frequency_hz", "Hz", {"low_hz": 8, "high_hz": 12}),
    ("iapf", "iapf_hz", "Hz", {}),
    ("theta_beta", "theta_beta", "dimensionless", {}),
]


def _wait_window(client, window_id):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        response = client.get(f"/api/analysis-windows/{window_id}")
        assert response.status_code == 200, response.text
        value = response.json()
        if value["state"] not in {"queued", "preparing_source", "computing_window"}:
            return value
        time.sleep(0.01)
    pytest.fail("scalar window did not finish")


@pytest.mark.parametrize("algorithm_id,field,unit,parameters", ALGORITHMS)
@pytest.mark.parametrize("gap,rejected", [(False, False), (True, False), (False, True)])
def test_scalar_real_file_api_matches_each_full_run_point(
    tmp_path, algorithm_id, field, unit, parameters, gap, rejected
):
    client, recording, service, runs, root = configure_api(tmp_path, gap=gap, rejected=rejected)
    before = source_hashes(root)
    config = {"algorithm_id": algorithm_id, "channel": "F3-REF", "mode": "dynamic",
              "time": {"start_s": 0, "end_s": recording.duration_s},
              "dynamic_window_s": 5, "refresh_step_s": 1, **parameters}
    try:
        response = client.post("/api/runs", json={"recording_id": recording.id,
            "analysis_type": "official_algorithm", "config": config})
        assert response.status_code == 202, response.text
        run = runs.process_next()
        assert run.status.value == "completed", run.error
        series = run.result_summary["metric"]["series"]
        assert series
        for index, frame in enumerate(series):
            ensured = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": algorithm_id, "config": config,
                "cursor_sample": round(frame["window_end_s"] * recording.sfreq),
                "request_id": f"{algorithm_id}-window-{index}"})
            assert ensured.status_code in {200, 202}, ensured.text
            window = _wait_window(client, ensured.json()["window_id"])
            DynamicWindowResponse.model_validate(window)
            states = {"Complete": "ready", "Partial": "ready", "Rejected": "rejected",
                      "Unavailable": "unavailable"}
            assert window["state"] == states[frame["analysis_state"]], window
            assert window["range"]["start_s"] == frame["window_start_s"]
            assert window["range"]["end_s"] == frame["window_end_s"]
            assert window["quality"] == frame["quality"]["status"]
            if window["state"] == "ready":
                result = window["result"]
                np.testing.assert_allclose(result["arrays"][field], [frame["output"]["value"]],
                                           rtol=1e-12, atol=1e-12)
                assert result["array_units"][field] == unit == frame["output"]["unit"]
                assert result["axes"]["time_s"] == [frame["window_end_s"]]
                for key in ("source_quality", "spectral_evidence", "calculation_trace"):
                    assert result["evidence"][key] == frame["official"][key]
                for name, value in result["evidence"]["extensions"].items():
                    assert value == frame["official"][name]
                assert result["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
                assert result["provenance"]["analysis_state"] == frame["analysis_state"]
            else:
                assert window["result"] is None
                assert frame["output"]["value"] is None
                assert window["failure"]["code"] == frame["failure"]["code"]
                assert window["failure"]["details"] == frame["failure"]["detail"]
            client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert source_hashes(root) == before
    finally:
        service.close(wait=True)
        client.close()

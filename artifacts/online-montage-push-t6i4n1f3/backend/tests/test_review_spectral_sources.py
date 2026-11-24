"""Check the paired inspector against real existing dynamic algorithm routes."""

import json

import numpy as np

from app.api.topomap import dynamic_router
from app.services.topomap import TopomapService
from app.api.result_scalars import router as scalar_router
from app.services.result_scalars import ResultScalarsService
from on_demand_fixtures import configure_api, psd_config, source_hashes
from test_on_demand_psd_api import wait_window


def test_real_psd_rbp_windows_share_context_and_projections_read_saved_arrays(tmp_path):
    client, recording, windows, runs, root = configure_api(tmp_path)
    app = client.app
    app.state.topomap_service = TopomapService(None, None, windows)
    app.include_router(dynamic_router)
    app.state.result_scalars_service = ResultScalarsService(windows)
    app.include_router(scalar_router)
    before = source_hashes(root)
    psd = psd_config(recording)
    psd.update(dynamic_window_s=10, notch_hz=50)
    rbp = {key: value for key, value in psd.items() if key not in {"low_hz", "high_hz"}}
    rbp.update(algorithm_id="rbp", channel=psd["channels"][0])
    results = {}
    bands = ("delta", "theta", "alpha", "beta", "gamma")
    try:
        for algorithm, config in (("psd", psd), ("rbp", rbp)):
            response = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": algorithm, "config": config,
                "cursor_sample": int(20 * recording.sfreq), "request_id": "inspect-" + algorithm,
            })
            assert response.status_code in {200, 202}, response.text
            window = wait_window(client, response.json()["window_id"])
            assert window["state"] == "ready", window
            results[algorithm] = window
        left, right = results["psd"], results["rbp"]
        assert left["range"] == right["range"]
        a = json.loads(left["scientific_configuration_json"])["context"]
        b = json.loads(right["scientific_configuration_json"])["context"]
        for key in ("sampling_rate_hz", "signal_mapping", "reference", "filters"):
            assert a[key] == b[key], key
        for key in ("welch_segment_s", "welch_overlap", "dynamic_window_s", "refresh_step_s", "alignment"):
            assert a["window"][key] == b["window"][key], key
        assert a["window"]["welch_segment_s"] == 4
        assert a["window"]["welch_overlap"] == 0.5
        assert a["window"]["dynamic_window_s"] == 10
        for algorithm, prefix, unit in (("psd", "psd.band-mean-density.", "V^2/Hz"),
                                        ("rbp", "rbp.amplitude.", "uV")):
            window = results[algorithm]
            response = client.get(f"/api/analysis-windows/{window['window_id']}/scalar-values",
                                  params=[("field_ids", prefix + band) for band in bands])
            assert response.status_code == 200, response.text
            fields = response.json()["fields"]
            spatial = client.get(f"/api/analysis-windows/{window['window_id']}/topomap",
                                 params=[("field_ids", prefix + band) for band in bands])
            assert spatial.status_code == 200, spatial.text
            assert len(fields) == 5
            for field, mapped, band in zip(fields, spatial.json()["fields"], bands, strict=True):
                assert field["unit"] == unit
                assert "grid" not in field
                assert [point["value"] for point in field["points"]] == [point["value"] for point in mapped["points"]]
                assert [point["channel"] for point in field["points"]] == psd["channels"]
                if algorithm == "rbp":
                    power = window["result"]["arrays"][band + "_absolute_power"][0]
                    assert field["points"][0]["value"] == np.sqrt(power)
            assert client.get(f"/api/analysis-windows/{window['window_id']}").json()["result"] == window["result"]
        assert source_hashes(root) == before
    finally:
        windows.close()
        client.close()

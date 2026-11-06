"""Opt-in synthetic saved-window benchmark; never opens the user's recordings."""

import json
import gc
import tempfile
from pathlib import Path
from statistics import median
from time import perf_counter

import numpy as np

from app.algorithms.rbp.custom_frequency import project_bins, project_interval, saved_denominators_uv2
from app.api.custom_frequency_inspection import router
from app.models.custom_frequency_inspection import CustomFrequencyWindowPair, CustomFrequencyRangeRequest
from app.services.custom_frequency_inspection import CustomFrequencyInspectionService
from on_demand_fixtures import configure_api
from test_on_demand_rbp_api import wait_window


def measure(action, count=20):
    elapsed = []
    for _ in range(count):
        started = perf_counter()
        action()
        elapsed.append((perf_counter() - started) * 1000)
    return {"median_ms": round(median(elapsed), 3),
            "p95_ms": round(float(np.percentile(elapsed, 95)), 3)}


def benchmark(root):
    labels = ["Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T3", "C3", "Cz",
              "C4", "T4", "M1", "T5", "P3", "Pz", "P4", "T6", "M2", "O1", "O2", "Oz"]
    pairs = [(label, "M2") for label in labels if label != "M2"]
    client, recording, windows, _, _ = configure_api(root, duration_s=12, labels=labels, montage_pairs=pairs)
    client.app.include_router(router)
    service = CustomFrequencyInspectionService(windows)
    client.app.state.custom_frequency_inspection_service = service
    saved = {}
    try:
        for algorithm in ("psd", "rbp"):
            channels = [f"{first}-{second}" for first, second in pairs]
            config = {"algorithm_id": algorithm, "channels": channels, "mode": "dynamic",
                      "time": {"start_s": 0, "end_s": 12}, "dynamic_window_s": 10,
                      "refresh_step_s": 1, "notch_hz": 50}
            config.update({"channel": channels[0]} if algorithm == "rbp" else {"low_hz": 1, "high_hz": 50})
            response = client.post("/api/analysis-windows/ensure", json={
                "recording_id": recording.id, "algorithm_id": algorithm, "config": config,
                "cursor_sample": 2000, "request_id": f"benchmark-{algorithm}"})
            assert response.status_code in {200, 202}, response.text
            saved[algorithm] = wait_window(client, response.json()["window_id"])
            assert saved[algorithm]["state"] == "ready", saved[algorithm]
        pair = CustomFrequencyWindowPair(psd_window_id=saved["psd"]["window_id"],
                                         rbp_window_id=saved["rbp"]["window_id"])
        interval = CustomFrequencyRangeRequest(**pair.model_dump(), low_hz=1.1, high_hz=2.9)
        store = windows._artifact_store
        def cold(action):
            store.clear_cache()
            return action()
        rbp_id = pair.rbp_window_id
        public = lambda: windows.get(rbp_id)
        bins = lambda: service.read_bins(pair)
        ranges = lambda: service.read_range(interval)
        result = {"channels": len(pairs), "bins": 197, "iterations": 20}
        for name, action in (("public_window", public), ("bins_service", bins), ("range_service", ranges)):
            result[name + "_cold"] = measure(lambda: cold(action))
            action()
            result[name + "_warm"] = measure(action)
        source = windows.get(rbp_id, include_projection_source=True)["result"]
        frequencies = source["axes"]["frequency_hz"]
        spectra = source["arrays"]["spectral_psd"]
        denominators = saved_denominators_uv2(source["arrays"], len(pairs))
        bounds = [(item.low_hz, item.high_hz) for item in bins().bin_intervals]
        result["former_scalar_bin_loop"] = measure(lambda: [
            project_interval(frequencies, row, total, low, high)
            for row, total in zip(spectra, denominators) for low, high in bounds])
        result["batched_bin_projection"] = measure(lambda: project_bins(frequencies, spectra, denominators, bounds))
        public_result = public()["result"]
        all_fields = {**source, "axes": {key: value.tolist() for key, value in source["axes"].items()},
                      "arrays": {key: value.tolist() for key, value in source["arrays"].items()}}
        compact = lambda value: len(json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode())
        result["rbp_result_json_bytes_public"] = compact(public_result)
        result["rbp_result_json_bytes_with_private_spectrum"] = compact(all_fields)
        for name, request in (("bins", pair), ("range", interval)):
            path = f"/api/analysis-windows/custom-frequency/{name}"
            response = client.post(path, json=request.model_dump())
            assert response.status_code == 200, response.text
            result[name + "_http_json_bytes"] = len(response.content)
            result[name + "_http_warm"] = measure(lambda: client.post(path, json=request.model_dump()))
        result["retained_cache_bytes"] = store._cache_bytes
        result["retained_cache_entries"] = len(store._read_cache)
        return result
    finally:
        windows.close(wait=True)
        client.close()


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="custom-frequency-benchmark-") as directory:
        result = benchmark(Path(directory))
        gc.collect()
        print(json.dumps(result, indent=2))

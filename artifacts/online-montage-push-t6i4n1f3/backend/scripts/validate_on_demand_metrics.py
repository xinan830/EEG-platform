"""Reproduce RBP/scalar equivalence through real WPF file and HTTP contracts.

Print a compact JSON report, without EEG samples or temporary/personal paths.
This is a migration comparison, not an independent scientific reference.
"""

from __future__ import annotations

import gc
import hashlib
import json
import platform
import sys
import tempfile
import time
from collections import Counter
from pathlib import Path

import numpy as np
import scipy

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tests"))

from app.algorithm_runtime.on_demand import DynamicWindowResponse
from app.bootstrap import build_builtin_registry
from app.scientific.contracts.types import SampleRange
from on_demand_fixtures import configure_api, source_hashes


ALGORITHMS = {
    "rbp": ({name: "ratio" for name in ("delta", "theta", "alpha", "beta", "gamma")}, {}),
    "band_ratio": ({"ratio": "ratio"}, {
        "numerator_low_hz": 8, "numerator_high_hz": 12,
        "denominator_low_hz": 15, "denominator_high_hz": 25}),
    "peak_frequency": ({"peak_frequency_hz": "Hz"}, {"low_hz": 8, "high_hz": 12}),
    "iapf": ({"iapf_hz": "Hz"}, {}),
    "theta_beta": ({"theta_beta": "dimensionless"}, {}),
}
SCENARIOS = [
    ("continuous", False, False, 0, 5),
    ("gap_9_to_10_s", True, False, 0, 5),
    ("amplitude_rejected", False, True, 0, 5),
    ("window_10_s", False, False, 0, 10),
    ("range_start_2_s", False, False, 2, 5),
]
RTOL = ATOL = 1e-12


def terminal(client, ensured):
    deadline = time.monotonic() + 30
    current = ensured
    while current["state"] in {"queued", "preparing_source", "computing_window"}:
        if time.monotonic() > deadline:
            raise TimeoutError("diagnostic window exceeded 30 s")
        time.sleep(0.01)
        response = client.get(f"/api/analysis-windows/{current['window_id']}")
        assert response.status_code == 200, response.text
        current = response.json()
    DynamicWindowResponse.model_validate(current)
    return current


def full_artifact_hashes(runs, run_id):
    result = {}
    for artifact in runs.list_artifacts(run_id):
        path = runs.base.artifacts.root / artifact.relative_path
        with path.open("rb") as stream:
            result[artifact.relative_path] = hashlib.file_digest(stream, "sha256").hexdigest()
    return result


def compare_frame(current, frame, fields, algorithm_id, sfreq, report):
    expected = {"Complete": "ready", "Partial": "ready",
                "Rejected": "rejected", "Unavailable": "unavailable"}[frame["analysis_state"]]
    assert current["state"] == expected
    assert current["quality"] == frame["quality"]["status"]
    actual_range = current["range"]
    assert actual_range["start_s"] == frame["window_start_s"]
    assert actual_range["end_s"] == frame["window_end_s"]
    samples = SampleRange.from_seconds(frame["window_start_s"], frame["window_end_s"], sfreq)
    assert (actual_range["start_sample"], actual_range["end_sample"]) == (samples.start_sample, samples.end_sample)
    report["states"][expected] += 1
    report["analysis_states"][frame["analysis_state"]] += 1
    if expected != "ready":
        assert current["result"] is None
        assert frame["value"] is None
        assert current["failure"]["code"] == frame["failure"]["code"]
        assert current["failure"]["details"] == (frame["failure"]["detail"] or {})
        report["failure_codes"][current["failure"]["code"]] += 1
        return

    result = current["result"]
    assert result["axes"] == {"time_s": [frame["window_end_s"]]}
    assert result["axis_units"] == {"time_s": "s"}
    assert result["array_units"] == fields
    assert set(result["arrays"]) == set(fields)
    for name in fields:
        reference = frame["band_values"][name] if algorithm_id == "rbp" else frame["value"]
        actual = np.asarray(result["arrays"][name])
        assert actual.shape == (1,)
        np.testing.assert_allclose(actual, [reference], rtol=RTOL, atol=ATOL)
        absolute = float(abs(actual[0] - reference))
        relative = absolute / max(abs(reference), ATOL)
        report["max_abs_error"] = max(report["max_abs_error"], absolute)
        report["max_relative_error"] = max(report["max_relative_error"], relative)
        report["compared_values"] += 1
    for key in ("source_quality", "spectral_evidence", "calculation_trace"):
        assert result["evidence"][key] == frame["official"][key]
    for name, value in result["evidence"]["extensions"].items():
        assert value == frame["official"][name]
    assert result["provenance"]["analysis_state"] == frame["analysis_state"]
    assert result["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"
    report["ready_evidence_compared"] += 1


def compare_run(client, recording, runs, algorithm_id, scenario, fields, parameters):
    name, _, _, start, window = scenario
    config = {"algorithm_id": algorithm_id, "channel": "F3-REF", "mode": "dynamic",
              "time": {"start_s": start, "end_s": recording.duration_s},
              "dynamic_window_s": window, "refresh_step_s": 1, **parameters}
    response = client.post("/api/runs", json={"recording_id": recording.id,
        "analysis_type": "official_algorithm", "config": config})
    assert response.status_code == 202, response.text
    run = runs.process_next()
    assert run.run_id == response.json()["run_id"] and run.status.value == "completed", run.error
    artifact_before = full_artifact_hashes(runs, run.run_id)
    series = run.result_summary["metric"]["series"]
    assert series
    report = {"scenario": name, "config": config, "windows": len(series),
              "states": Counter(), "analysis_states": Counter(), "failure_codes": Counter(),
              "compared_values": 0, "ready_evidence_compared": 0,
              "max_abs_error": 0.0, "max_relative_error": 0.0}
    for index, frame in enumerate(series):
        ensured = client.post("/api/analysis-windows/ensure", json={"recording_id": recording.id,
            "algorithm_id": algorithm_id, "config": config,
            "cursor_sample": round(frame["window_end_s"] * recording.sfreq),
            "request_id": f"diagnostic-{name}-{algorithm_id}-{index}"})
        assert ensured.status_code in {200, 202}, ensured.text
        current = terminal(client, ensured.json())
        compare_frame(current, frame, fields, algorithm_id, recording.sfreq, report)
        released = client.delete(f"/api/analysis-windows/subscriptions/{ensured.json()['subscription_id']}")
        assert released.status_code == 200, released.text
    assert full_artifact_hashes(runs, run.run_id) == artifact_before
    report["full_artifacts_count"] = len(artifact_before)
    report["full_artifacts_unchanged"] = True
    report["last_window_end_s"] = series[-1]["window_end_s"]
    report["uncovered_tail_s"] = recording.duration_s - series[-1]["window_end_s"]
    return report


def main():
    registry = build_builtin_registry()
    report = {"schema": "on-demand-metrics-equivalence-v1", "rtol": RTOL, "atol": ATOL,
              "relative_error_denominator": "max(abs(reference), atol)",
              "fixture": {"sfreq_hz": 200, "duration_s": 22.35, "first_counter": 700,
                          "available_channels": ["F3-REF", "F7-REF"], "tested_channel": "F3-REF"},
              "environment": {"python": platform.python_version(), "numpy": np.__version__,
                              "scipy": scipy.__version__}, "algorithms": {}}
    for algorithm_id, (fields, _) in ALGORITHMS.items():
        manifest = registry.get(algorithm_id).manifest
        report["algorithms"][algorithm_id] = {"scientific_version": manifest.scientific_version,
            "implementation_identity": manifest.implementation_identity, "units": fields, "cases": []}
    with tempfile.TemporaryDirectory(prefix="brain-metrics-validation-") as temporary:
        for index, scenario in enumerate(SCENARIOS):
            scratch = Path(temporary) / f"case-{index}"
            scratch.mkdir()
            client, recording, service, runs, root = configure_api(scratch, gap=scenario[1], rejected=scenario[2])
            before = source_hashes(root)
            try:
                for algorithm_id, (fields, parameters) in ALGORITHMS.items():
                    result = compare_run(client, recording, runs, algorithm_id, scenario, fields, parameters)
                    assert source_hashes(root) == before
                    result["source_unchanged"] = True
                    report["algorithms"][algorithm_id]["cases"].append(result)
            finally:
                service.close(wait=True)
                client.close()
                del runs, service, client
                gc.collect()
    report["passed"] = True
    print(json.dumps(report, indent=2, ensure_ascii=True))


if __name__ == "__main__":
    main()

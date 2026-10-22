"""Replay all current result families against pre-refactor Run/artifact snapshots."""

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from result_boundary_support import compare_value, snapshot_run


ROOT = Path(__file__).resolve().parents[2]
REPORT = json.loads((ROOT / "docs/validation/algorithm-result-boundary-baseline.json").read_text())
ARTIFACT = ROOT / REPORT["artifact"]["path"]
ORIGINAL_REPORT = json.loads((ROOT / "docs/validation/official-algorithm-strategy-baseline.json").read_text())
ORIGINAL_ARTIFACT = ROOT / ORIGINAL_REPORT["artifact"]["path"]


def current_contract_expectation(snapshot):
    """Project only the approved wire-shape changes in the frozen test oracle.

    This is test-only: frozen bytes, numeric arrays, coordinates, states, quality
    and scientific provenance are never regenerated or normalized.
    """
    summary = snapshot["summary"]
    if snapshot["status"] == "completed":
        summary["result_contract_version"] = "official-algorithm-result-v1"
    metric = summary.get("metric")
    if metric:
        for item in [metric, *metric.get("series", [])]:
            if "value" in item:
                assert item.pop("value") == item["output"]["value"]
            item.pop("warmup", None)
            if "result_contract_version" in item:
                assert item["result_contract_version"] == "dynamic-analysis-frame-v5"
                item["result_contract_version"] = "dynamic-analysis-frame-v6"
    return snapshot


@pytest.mark.parametrize("case", REPORT["cases"], ids=lambda case: case["case"])
def test_current_run_and_artifact_match_frozen_boundary(tmp_path, case, record_property):
    assert hashlib.sha256(ARTIFACT.read_bytes()).hexdigest() == REPORT["artifact"]["sha256"]
    actual, arrays = snapshot_run(tmp_path, case["algorithm_id"], case["mode"], case["rejected"])
    diagnostics = []
    with np.load(ARTIFACT, allow_pickle=False) as baseline:
        prefix = case["case"] + "__"
        expected = current_contract_expectation(json.loads(str(baseline[prefix + "snapshot_json"].item())))
        errors = compare_value(actual, expected)
        assert set(arrays) == set(case["array_keys"])
        for name, values in arrays.items():
            wanted = baseline[prefix + name]
            assert values.shape == wanted.shape, name
            if values.dtype.kind in "US":
                errors.extend(compare_value(json.loads(str(values.item())), json.loads(str(wanted.item())), path=name))
                continue
            passing = np.isclose(values, wanted, **REPORT["tolerances"], equal_nan=True)
            if name.startswith("axis_") or name == "metric_time_s":
                assert np.array_equal(values, wanted, equal_nan=True), name
            finite = np.isfinite(values) & np.isfinite(wanted)
            delta = np.abs(values[finite] - wanted[finite])
            relative = np.divide(delta, np.abs(wanted[finite]),
                                 out=np.where(delta == 0, 0.0, np.inf), where=wanted[finite] != 0)
            row = {"array": name, "maximum_absolute_error": float(delta.max()) if delta.size else 0.0,
                   "maximum_relative_error": float(relative.max()) if relative.size else 0.0,
                   "failed_coordinates": np.argwhere(~passing).tolist(), "point_count": int(values.size)}
            diagnostics.append(row)
            assert passing.all(), row
            if case["mode"] == "dynamic" and not case["rejected"]:
                assert hashlib.sha256(ORIGINAL_ARTIFACT.read_bytes()).hexdigest() == ORIGINAL_REPORT["artifact"]["sha256"]
                with np.load(ORIGINAL_ARTIFACT, allow_pickle=False) as original:
                    historical = original[case["algorithm_id"] + "__" + name]
                    assert historical.shape == values.shape, name
                    np.testing.assert_allclose(values, historical, **ORIGINAL_REPORT["tolerances"], equal_nan=True)
    record_property("array_comparison", json.dumps(diagnostics))
    record_property("summary_comparison", json.dumps({
        "maximum_absolute_error": max((item["absolute_error"] for item in errors), default=0.0),
        "maximum_relative_error": max((item["relative_error"] for item in errors), default=0.0),
        "failed_positions": [item["path"] for item in errors if not item["passed"]],
        "numeric_fields": len(errors),
    }))

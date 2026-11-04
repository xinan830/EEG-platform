"""Replay all current result families against pre-refactor Run/artifact snapshots."""

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from result_boundary_support import compare_value, snapshot_run


ROOT = Path(__file__).resolve().parents[2]
REPORT = json.loads((ROOT / "docs/validation/algorithm-result-boundary-v4-baseline.json").read_text())
ARTIFACT = ROOT / REPORT["artifact"]["path"]
def current_contract_expectation(snapshot):
    """V4 snapshots already use the current wire and scientific contracts."""
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
    record_property("array_comparison", json.dumps(diagnostics))
    record_property("summary_comparison", json.dumps({
        "maximum_absolute_error": max((item["absolute_error"] for item in errors), default=0.0),
        "maximum_relative_error": max((item["relative_error"] for item in errors), default=0.0),
        "failed_positions": [item["path"] for item in errors if not item["passed"]],
        "numeric_fields": len(errors),
    }))

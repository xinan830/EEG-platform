"""Freeze current contracts and retain unchanged numerical evidence from v4."""

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest

from result_boundary_support import compare_value, snapshot_run, validate_current_scalar_reference
from app.core.provenance import sha256_json


ROOT = Path(__file__).resolve().parents[2]
REPORT = json.loads((Path(__file__).parent / "fixtures/algorithm-result-boundary-20261127.json").read_text(encoding="utf-8"))
ARTIFACT = ROOT / REPORT["artifact"]["path"]
HISTORICAL = json.loads((ROOT / "docs/validation/algorithm-result-boundary-v4-baseline.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize("case", REPORT["cases"], ids=lambda case: case["case"])
def test_current_run_and_artifact_match_frozen_boundary(tmp_path, case, record_property):
    assert hashlib.sha256(ARTIFACT.read_bytes()).hexdigest() == REPORT["artifact"]["sha256"]
    actual, arrays = snapshot_run(tmp_path, case["algorithm_id"], case["mode"], case["rejected"])
    validate_current_scalar_reference(actual, arrays)
    diagnostics = []
    with np.load(ARTIFACT, allow_pickle=False) as baseline:
        prefix = case["case"] + "__"
        expected = json.loads(str(baseline[prefix + "snapshot_json"].item()))
        assert actual["analysis_provenance"]["config_sha256"] == sha256_json(actual["resolved_config"])
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


@pytest.mark.parametrize("case", [case for case in HISTORICAL["cases"] if case["algorithm_id"] in {
    "band_ratio", "faa", "peak_frequency", "psd", "rbp", "stft",
}], ids=lambda case: case["case"])
def test_unchanged_algorithms_retain_historical_numeric_arrays(case):
    historical_path = ROOT / HISTORICAL["artifact"]["path"]
    assert hashlib.sha256(historical_path.read_bytes()).hexdigest() == HISTORICAL["artifact"]["sha256"]
    assert hashlib.sha256(ARTIFACT.read_bytes()).hexdigest() == REPORT["artifact"]["sha256"]
    with np.load(historical_path, allow_pickle=False) as old, np.load(ARTIFACT, allow_pickle=False) as current:
        snapshot_key = case["case"] + "__snapshot_json"
        prior = json.loads(str(old[snapshot_key].item()))
        config = json.loads(str(current[snapshot_key].item()))["resolved_config"]
        assert config.pop("preprocessing") is None
        assert config.pop("frequency_mode") == "standard"
        assert sha256_json(config) == prior["analysis_provenance"]["config_sha256"]
        for name in case["array_keys"]:
            key = case["case"] + "__" + name
            if old[key].dtype.kind not in "US":
                np.testing.assert_allclose(current[key], old[key], **HISTORICAL["tolerances"], equal_nan=True)

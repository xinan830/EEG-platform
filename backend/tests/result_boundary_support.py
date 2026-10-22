"""Deterministic synthetic Run snapshots for result-boundary regression."""

import hashlib
import json
import re

import numpy as np

from app.services.analysis_provenance import build_analysis_provenance
from test_official_algorithm_runs import _request, _service


ALGORITHM_IDS = (
    "band_ratio", "brainbeat", "faa", "iapf", "peak_frequency",
    "psd", "rbp", "stft", "theta_beta",
)


def snapshot_run(tmp_path, algorithm_id, mode, rejected, provenance_builder=build_analysis_provenance):
    service, recording_id = _service(tmp_path, sfreq_hz=200.0)
    original = service.recordings.load_data

    def source(recording):
        values, rate, channels, annotations = original(recording)
        return values * (30 if rejected else 1), rate, channels, annotations

    service.recordings.load_data = source
    request = _request(recording_id, algorithm_id, dynamic=mode == "dynamic")
    if algorithm_id == "band_ratio":
        request.config.update(numerator_low_hz=8, numerator_high_hz=13,
                              denominator_low_hz=13, denominator_high_hz=30)
    if algorithm_id == "peak_frequency":
        request.config.update(low_hz=8, high_hz=13)
    if algorithm_id == "brainbeat":
        request.config["secondary_channel"] = "Pz"
    if algorithm_id == "faa" and mode == "dynamic":
        request.config["dynamic_window_s"] = 20
    run = service.create(request)
    assert run.status.value in {"completed", "gate_failed"}, run.error
    artifacts = service.list_artifacts(run.run_id)
    artifact = artifacts[0] if artifacts else None
    if artifact is not None:
        service.artifacts.verify(artifact)
    arrays = service.artifacts.read_npz(artifact) if artifact else {}
    summary = {key: value for key, value in run.result_summary.items() if key != "artifacts"}
    provenance = provenance_builder(run)
    # These are storage allocation/cache addresses, not scientific coordinates.
    # Retain the immutable Definition version and all scientific identity fields.
    provenance["official_algorithm_identity"]["definition_id"] = "<allocated-definition-id>"
    provenance["scientific_cache_key"] = "<allocated-cache-key>"
    values, rate, channels, _ = source(service.recordings.require_recording(recording_id))
    snapshot = {
        "input": {"sha256": hashlib.sha256(values.astype("<f8").tobytes()).hexdigest(),
                  "sfreq_hz": rate, "channel_order": channels, "unit": "V"},
        "config": request.config, "status": run.status.value,
        "error": run.error.model_dump(mode="json") if run.error else None,
        "summary": summary,
        "analysis_provenance": provenance,
        "artifact_metadata": {"kind": artifact.kind, "unit": artifact.unit,
                              "shape": artifact.shape, "media_type": artifact.media_type} if artifact else None,
    }
    return snapshot, arrays


def compare_value(actual, expected, *, path="$", errors=None):
    """Exact metadata comparison with tolerances only for numeric values."""
    errors = errors if errors is not None else []
    if isinstance(expected, dict):
        assert isinstance(actual, dict) and actual.keys() == expected.keys(), path
        for key in expected:
            compare_value(actual[key], expected[key], path=f"{path}.{key}", errors=errors)
    elif isinstance(expected, list):
        assert isinstance(actual, list) and len(actual) == len(expected), path
        for index, value in enumerate(expected):
            compare_value(actual[index], value, path=f"{path}[{index}]", errors=errors)
    elif isinstance(expected, (float, int)) and not isinstance(expected, bool):
        # Coordinates, counts and identities must remain exact. Only output
        # scientific values/intermediates may use the frozen numerical tolerance.
        numeric = any(part in path for part in (".value", "power", "ratio", "peak_hz", "cog", "r_squared", "error"))
        if numeric:
            passed = bool(np.isclose(actual, expected, rtol=1e-10, atol=1e-20, equal_nan=True))
            delta = abs(actual - expected)
            errors.append({"path": path, "absolute_error": delta,
                           "relative_error": delta / abs(expected) if expected else (0.0 if delta == 0 else float("inf")),
                           "passed": passed})
            assert passed, (path, actual, expected)
        else:
            assert actual == expected, (path, actual, expected)
    else:
        if path.endswith(".montage_id") and isinstance(expected, str) and re.fullmatch(r"raw-recording:[0-9a-f]{32}", expected):
            assert isinstance(actual, str) and re.fullmatch(r"raw-recording:[0-9a-f]{32}", actual), path
            return errors
        assert actual == expected, (path, actual, expected)
    return errors


def canonical_json(value):
    return json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":"))

"""Check persisted values, quality, ranges and artifacts against independent science."""

from __future__ import annotations

import json
from collections import Counter

import numpy as np

from real_recording_reference import BANDS, faa, metric, shares, spectrogram, welch


class Comparisons:
    def __init__(self):
        self.outputs = {}

    def check(self, name: str, actual, expected, *, unit: str, atol: float = 1e-24):
        actual, expected = np.asarray(actual, dtype=float), np.asarray(expected, dtype=float)
        assert actual.shape == expected.shape, f"{name}: shape {actual.shape} != {expected.shape}"
        assert np.array_equal(np.isnan(actual), np.isnan(expected)), f"{name}: unavailable mask mismatch"
        assert np.isfinite(actual[~np.isnan(actual)]).all(), f"{name}: infinite result"
        np.testing.assert_allclose(actual, expected, rtol=1e-8, atol=atol, equal_nan=True, err_msg=name)
        finite = np.isfinite(expected)
        errors = np.abs(actual[finite] - expected[finite])
        relative = errors / np.maximum(np.abs(expected[finite]), 1e-30)
        stats = self.outputs.setdefault(name, {"unit": unit, "finite_points": 0, "unavailable_points": 0,
                                             "max_absolute_error": 0.0, "max_relative_error": 0.0,
                                             "rtol": 1e-8, "atol": atol})
        stats["finite_points"] += int(finite.sum())
        stats["unavailable_points"] += int(np.isnan(expected).sum())
        stats["max_absolute_error"] = max(stats["max_absolute_error"], float(errors.max()) if errors.size else 0)
        stats["max_relative_error"] = max(stats["max_relative_error"], float(relative.max()) if relative.size else 0)


def _check_quality(actual: dict, expected: dict):
    for key in ("total_segments", "clean_segments"):
        assert actual.get(key) == expected[key], f"quality {key}: {actual.get(key)} != {expected[key]}"
    assert abs(float(actual["clean_ratio"]) - expected["clean_ratio"]) <= 1e-12
    if "rejected_reasons" in expected:
        assert set(actual.get("rejected_reasons", [])) == set(expected["rejected_reasons"])


def _bounds(point: dict, structured: bool) -> tuple[float, float]:
    return (float(point["start_s"]), float(point["end_s"])) if structured else (
        float(point["window_start_s"]), float(point["window_end_s"]))


def _slice(values: np.ndarray, start_s: float, end_s: float, sfreq: float, algorithm: str) -> np.ndarray:
    start = int(np.floor(start_s * sfreq))
    stop = int(round(end_s * sfreq)) if algorithm == "faa" else start + int(round((end_s - start_s) * sfreq))
    return values[start:stop]


def _bands(actual: dict | None, expected: dict | None, comparisons: Comparisons):
    assert actual is not None and set(actual) == set(BANDS), "RBP missing output bands"
    a = [np.nan if actual[name] is None else actual[name] for name in BANDS]
    e = [np.nan if expected is None else expected[name] for name in BANDS]
    comparisons.check("band_values", a, e, unit="ratio", atol=1e-12)
    if expected is not None:
        assert all(0 <= value <= 1 for value in a)
        assert abs(sum(a) - 1) < 1e-12


def _audit_metric(body: dict, arrays: dict, config: dict, values: np.ndarray, sfreq: float,
                  comparisons: Comparisons) -> list[dict]:
    algorithm, dynamic = config["algorithm_id"], config["mode"] == "dynamic"
    points = body["series"] if dynamic else [body]
    for index, point in enumerate(points):
        start, end = _bounds(point, False) if dynamic else (config["time"]["start_s"], config["time"]["end_s"])
        window = _slice(values, start, end, sfreq, algorithm)
        expected, quality = faa(window, sfreq) if algorithm == "faa" else metric(algorithm, window, sfreq, config)
        source_quality = point.get("source_quality") or (point.get("failure") or {}).get("detail", {})
        _check_quality(source_quality, quality)
        if algorithm == "rbp":
            _bands(point.get("band_values"), expected, comparisons)
            if not dynamic:
                comparisons.check("band_share_values_artifact", arrays["band_share_values"],
                                  [np.nan if expected is None else expected[name] for name in BANDS],
                                  unit="ratio", atol=1e-12)
        else:
            actual = point["output"]["value"]
            comparisons.check("metric", [np.nan if actual is None else actual],
                              [np.nan if expected is None else expected], unit=point["output"]["unit"], atol=1e-12)
            persisted = arrays["metric_values"][index] if dynamic else arrays["metric_value"][0]
            comparisons.check("metric_artifact", [persisted], [np.nan if expected is None else expected],
                              unit=point["output"]["unit"], atol=1e-12)
        if expected is None:
            assert point.get("failure"), "unavailable scientific value has no structured reason"
        else:
            assert point["output"]["quality"]["status"] == "clean" and point.get("failure") is None
    return points if dynamic else []


def _stft_arrays(window: np.ndarray, sfreq: float, start_s: float, comparisons: Comparisons,
                 actual: dict, inner_count: int | None = None, actual_quality: dict | None = None):
    _, density, _ = welch(window, sfreq, high=50.0)
    if density is None:
        assert inner_count is not None, "static STFT should have failed its aggregate quality gate"
        expected = np.full(actual["power_linear"].shape, np.nan)
    else:
        centers, freqs, rows = spectrogram(window, sfreq)
        rows = rows[:, 0, :]
        if inner_count is None:
            comparisons.check("time_axis", actual["axis_time_center_s"], centers + start_s, unit="s", atol=1e-12)
            comparisons.check("frequency_axis", actual["axis_frequency_hz"], freqs, unit="Hz", atol=0)
            assert actual_quality["total_windows"] == len(rows)
            assert actual_quality["bad_windows"] == int(np.isnan(rows).all(axis=1).sum())
        expected = rows
        if inner_count is not None:
            expected = np.full((inner_count, rows.shape[1]), np.nan)
            expected[:len(rows)] = rows
    comparisons.check("power_linear", actual["power_linear"], expected, unit="V^2/Hz")
    db = 10 * np.log10(np.maximum(expected * 1e12, np.finfo(float).tiny))
    comparisons.check("power_db", actual["power_db"], db, unit="dB re 1 uV^2/Hz", atol=1e-8)


def _audit_structured(body: dict, arrays: dict, config: dict, values: np.ndarray, sfreq: float,
                      comparisons: Comparisons) -> list[dict]:
    algorithm, dynamic = config["algorithm_id"], config["mode"] == "dynamic"
    points = body["windows"] if dynamic else [{}]
    evidence = json.loads(str(arrays["window_evidence_json"].item())) if dynamic else [body]
    for index, point in enumerate(points):
        start, end = _bounds(point, True) if dynamic else (config["time"]["start_s"], config["time"]["end_s"])
        window = _slice(values, start, end, sfreq, algorithm)
        if algorithm == "psd":
            freqs, density, quality = welch(window, sfreq, high=50.0)
            _check_quality(evidence[index]["source_quality"], quality)
            expected = np.full(freqs.shape, np.nan) if density is None else density[0]
            comparisons.check("psd", arrays["psd"][index] if dynamic else arrays["psd"], expected, unit="V^2/Hz")
            comparisons.check("frequency_axis", arrays["axis_frequency_hz"], freqs, unit="Hz", atol=0)
            band_values = [np.nan] * 5 if density is None else list(shares(freqs, density).values())
            comparisons.check("band_share", arrays["band_share"][index] if dynamic else arrays["band_share"],
                              band_values, unit="ratio", atol=1e-12)
            if dynamic:
                assert (point["state"] == "Rejected") == (density is None)
        else:
            if dynamic:
                inner_count = int(np.floor(config["dynamic_window_s"] - 4.0) + 1)
                actual = {name: arrays[name][index] for name in ("power_linear", "power_db")}
                _stft_arrays(window, sfreq, start, comparisons, actual, inner_count)
            else:
                _stft_arrays(window, sfreq, start, comparisons, arrays, actual_quality=body["source_quality"])
    return points if dynamic else []


def _check_windows(points: list[dict], config: dict, sfreq: float, minimum_s: float, warmup: bool, structured: bool):
    start, end = config["time"]["start_s"], config["time"]["end_s"]
    width, step = config["dynamic_window_s"], config["refresh_step_s"]
    first_end = min(minimum_s, width) if warmup and start == 0 else start + width
    endpoints = np.arange(first_end, end + 1e-9, step)
    assert len(points) == len(endpoints), f"window count {len(points)} != {len(endpoints)}"
    for point, endpoint in zip(points, endpoints):
        a, b = _bounds(point, structured)
        assert abs(b - endpoint) < 1e-9 and abs(a - max(start, endpoint - width)) < 1e-9
        if structured:
            assert point["start_sample"] == int(np.floor(a * sfreq))
            assert point["end_sample"] == int(np.floor(b * sfreq))
        else:
            assert point["time_s"] == b
            assert point["warmup"] == (b - a < width - 1e-9)


def audit_run(run, runs, config: dict, values: np.ndarray, sfreq: float, minimum_s: float, warmup: bool) -> dict:
    assert run.status.value in {"completed", "gate_failed"}, str(run.error)
    assert run.reference.get("mode") == "acquisition_montage_snapshot"
    assert run.channel_mapping["signal_source"]["signal_mode"] == "acquisition_montage"
    assert run.definition_id and run.definition_version and run.scientific_version and run.config_sha256
    summary = run.result_summary or {}
    structured = isinstance(summary.get("structured"), dict)
    body = summary.get("structured") or summary.get("metric") or {}
    comparisons = Comparisons()
    artifacts = runs.list_artifacts(run.run_id)
    arrays, artifact_checks = {}, []
    for artifact in artifacts:
        runs.artifacts.verify(artifact)
        loaded = runs.artifacts.read_npz(artifact)
        arrays.update(loaded)
        for array in loaded.values():
            if np.issubdtype(array.dtype, np.number):
                assert not np.isinf(array).any()
        artifact_checks.append({"sha256": artifact.sha256, "shapes": {key: list(value.shape) for key, value in loaded.items()}})
    if run.status.value == "gate_failed":
        _, density, evidence = welch(values, sfreq, high=50.0 if config["algorithm_id"] in {"psd", "rbp", "stft"} else 30.0)
        assert density is None, "production rejected a range the independent quality gate accepts"
        return {"run_id": run.run_id, "status": "gate_failed", "validation": "PASS", "source_quality": evidence}
    assert artifacts, "completed Run has no artifact"
    points = (_audit_structured if structured else _audit_metric)(body, arrays, config, values, sfreq, comparisons)
    if points:
        _check_windows(points, config, sfreq, minimum_s, warmup, structured)
    states = Counter(point["state"] if structured else point["analysis_state"] for point in points)
    failures = Counter(point["failure"]["code"] for point in points if point.get("failure"))
    rejected = []
    for index, point in enumerate(points):
        if point.get("failure"):
            source = point.get("source_quality") or point["failure"].get("detail", {})
            if structured and "window_evidence_json" in arrays:
                source = json.loads(str(arrays["window_evidence_json"].item()))[index].get("source_quality", {})
            rejected.append({"range_s": list(_bounds(point, structured)), "code": point["failure"]["code"],
                             "source_quality": source})
    source_quality = body.get("source_quality", {})
    # STFT quality rows are large; full matrices have already been verified.
    source_quality = {key: value for key, value in source_quality.items() if key != "windows"}
    return {
        "run_id": run.run_id, "status": run.status.value, "validation": "PASS", "config": config,
        "scientific_version": run.scientific_version, "implementation_identity": run.implementation_version,
        "config_sha256": run.config_sha256, "definition_id": run.definition_id,
        "definition_version": run.definition_version, "filters": run.filters, "reference": run.reference["mode"],
        "requested_range": config["time"], "summary_range": body.get("actual_range"),
        "actual_dynamic_end_s": _bounds(points[-1], structured)[1] if points else None,
        "static_value": body.get("output", {}).get("value") if not points else None,
        "static_band_values": body.get("band_values"),
        "static_quality": body.get("quality") if not points else None,
        "first_window_output_quality": body.get("output", {}).get("quality") if points else None,
        "window_count": len(points), "window_states": dict(states), "failure_counts": dict(failures),
        "source_quality": source_quality, "rejected_windows": rejected,
        "artifact_checks": artifact_checks, "numerical_comparisons": comparisons.outputs,
    }

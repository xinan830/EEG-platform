"""Synthetic inputs and pointwise diagnostics for strategy integration checks."""

import json

import numpy as np

from app.models.run import RunCreateRequest
from on_demand_fixtures import configure_api


ALGORITHM_IDS = (
    "psd", "stft", "rbp", "band_ratio", "peak_frequency", "iapf",
    "theta_beta", "faa", "brainbeat",
)
RTOL = 1e-10
ATOL = 1e-20


def setup_strategy(tmp_path, algorithm_id, *, rejected=False):
    labels = ["Fz", "Pz"] if algorithm_id == "brainbeat" else ["F3", "F4"]
    services = configure_api(tmp_path, duration_s=30, labels=labels, rejected=rejected)
    _, recording, _, _, _ = services
    config = {
        "algorithm_id": algorithm_id, "channel": f"{labels[0]}-REF", "mode": "dynamic",
        "time": {"start_s": 0, "end_s": 30},
        "dynamic_window_s": 10, "refresh_step_s": 2,
    }
    if algorithm_id in {"psd", "stft", "faa"}:
        config.update(low_hz=1, high_hz=30, notch_hz=None)
    if algorithm_id == "faa":
        config["f4_channel"] = "F4-REF"
        config["dynamic_window_s"] = 20
    if algorithm_id == "brainbeat":
        config["secondary_channel"] = "Pz-REF"
    if algorithm_id == "band_ratio":
        config.update(numerator_low_hz=8, numerator_high_hz=12,
                      denominator_low_hz=15, denominator_high_hz=25)
    if algorithm_id == "peak_frequency":
        config.update(low_hz=8, high_hz=12)
    return services, config


def uncached_full(runs, recording, config):
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config=config)
    resolved = runs.base._resolve_request(request, recording)
    summary, arrays, _ = runs.base.executor.execute(request.analysis_type, recording, resolved)
    return request, summary, arrays


def compare_arrays(actual, expected):
    """Report max error and first failed coordinates, not only an aggregate PASS."""
    assert actual.keys() == expected.keys()
    diagnostics = {}
    for key, wanted in expected.items():
        got = np.asarray(actual[key])
        wanted = np.asarray(wanted)
        assert got.shape == wanted.shape, key
        if got.dtype.kind in "USO":
            assert json.loads(str(got.item())) == json.loads(str(wanted.item())), key
            continue
        passing = np.isclose(got, wanted, rtol=RTOL, atol=ATOL, equal_nan=True)
        finite = np.isfinite(got) & np.isfinite(wanted)
        delta = np.abs(got[finite] - wanted[finite])
        denominator = np.abs(wanted[finite])
        relative = np.divide(delta, denominator, out=np.where(delta == 0, 0.0, np.inf), where=denominator != 0)
        failures = np.argwhere(~passing).tolist()
        diagnostics[key] = {"max_absolute_error": float(delta.max()) if delta.size else 0.0,
                            "max_relative_error": float(relative.max()) if relative.size else 0.0,
                            "failed_point_count": len(failures),
                            "failed_coordinates": failures[:10], "point_count": int(got.size)}
        assert passing.all(), (key, diagnostics[key])
    return diagnostics


def validation_context(recording, config, full, artifact):
    return {
        "source_sha256": recording.source_sha256, "sampling_rate_hz": recording.sfreq,
        "config": config, "scientific_version": full.scientific_version,
        "implementation_identity": full.result_summary["implementation_identity"],
        "artifact_sha256": artifact.sha256, "actual_range": full.actual_range,
        "execution_provenance": full.result_summary["execution_provenance"],
    }


def ensure_preview(client, recording, config):
    response = client.post("/api/analysis-windows/ensure", json={
        "recording_id": recording.id, "algorithm_id": config["algorithm_id"],
        "config": config, "cursor_sample": int(config["dynamic_window_s"] * recording.sfreq),
        "request_id": "synthetic-combined-preview",
    })
    assert response.status_code in {200, 202}, response.text
    return response.json()


def compare_summary(actual, expected):
    for section in ("structured", "metric"):
        if section not in expected:
            continue
        got, wanted = dict(actual[section]), dict(expected[section])
        if section == "structured":
            got_windows, wanted_windows = got.pop("windows"), wanted.pop("windows")
            assert len(got_windows) == len(wanted_windows)
            for got_window, wanted_window in zip(got_windows, wanted_windows, strict=True):
                current, baseline = dict(got_window), dict(wanted_window)
                current_provenance = current.pop("provenance")
                baseline_provenance = baseline.pop("provenance")
                assert current == baseline
                for key, value in baseline_provenance.items():
                    assert current_provenance[key] == value, key
                if "producer_mode" in current_provenance:
                    assert current_provenance["producer_mode"] == "on_demand"
                    assert current_provenance["served_from"] == "on_demand_computation"
        assert got == wanted


def compare_preview(preview, summary, arrays):
    result = preview["result"]
    end_s = preview["range"]["end_s"]
    if preview["state"] != "ready":
        assert result is None
        assert preview["state"] in {"rejected", "unavailable"}
        points = summary["structured"]["windows"] if "structured" in summary else summary["metric"]["series"]
        frame = next(point for point in points if point.get("window_end_s", point.get("end_s")) == end_s)
        assert frame["failure"] is not None
        assert preview["failure"]["code"] == frame["failure"]["code"]
        return
    if "structured" in summary:
        full = summary["structured"]
        index = next(i for i, window in enumerate(full["windows"]) if window["end_s"] == end_s)
        assert result["quality"] == full["windows"][index]["quality"]
        for key, values in result["arrays"].items():
            compare_arrays({key: values}, {key: arrays[key][index]})
        for key, values in result["axes"].items():
            compare_arrays({key: values}, {key: arrays[f"axis_{key}"]})
        evidence = json.loads(str(arrays["window_evidence_json"].item()))
        assert result["evidence"] == evidence[index]
    else:
        frame = next(point for point in summary["metric"]["series"] if point["window_end_s"] == end_s)
        assert frame["window_start_s"] == preview["range"]["start_s"]
        assert result["quality"] == frame["quality"]["status"]
        outputs = frame.get("band_values") or None
        for key, values in result["arrays"].items():
            wanted = outputs[key] if outputs is not None else frame["value"]
            compare_arrays({key: values}, {key: [wanted]})
        for key in ("source_quality", "spectral_evidence", "calculation_trace"):
            assert result["evidence"].get(key, {}) == frame[key]
        for key, value in result["evidence"].get("extensions", {}).items():
            assert value == frame["official"][key]
    if result["identity"]["algorithm_id"] != "psd":
        assert result["provenance"]["analysis_state"] == "Complete"
    assert result["provenance"]["preprocessing_scope"] == "whole_recording_before_window_slice"

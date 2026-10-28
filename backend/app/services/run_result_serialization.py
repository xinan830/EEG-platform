"""Serialize typed algorithm outputs without interpreting algorithm identity."""

import json
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (
    DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
    AlgorithmEvidence, AlgorithmResult, AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult, AlgorithmSeriesResult,
)
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig


def _public_evidence(evidence: dict[str, Any]) -> dict[str, Any]:
    """Expose common evidence plus named extensions without leaking a generic bag."""
    parsed = AlgorithmEvidence.model_validate(evidence)
    return {
        "source_quality": parsed.source_quality,
        "spectral_evidence": parsed.spectral_evidence,
        "calculation_trace": parsed.calculation_trace,
        **parsed.extensions,
    }


def _serialize_algorithm_result(result: AlgorithmResult, algorithm_id: str, label: str) -> dict[str, object]:
    evidence = _public_evidence(result.evidence)
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    payload: dict[str, object] = {
        "output": {"id": algorithm_id, "label": label, "value": result.value, "unit": result.unit, "quality": quality},
        "channel": result.channel,
        "actual_range": result.actual_range,
        "requested_range": result.requested_range,
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "chart": {"kind": "none"},
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }
    if result.output_values is not None:
        payload["band_values"] = result.output_values
        payload["chart"] = {"kind": "band_share", "values": result.output_values, "unit": result.unit}
    return payload


def _structured_artifact_arrays(
    result: AlgorithmStructuredResult | AlgorithmStructuredSeriesResult,
) -> tuple[dict[str, np.ndarray], dict[str, str], dict[str, dict[str, object]]]:
    """Pack declared axes and arrays once, preserving their units and coordinates."""
    arrays = {name: np.asarray(value) for name, value in result.arrays.items()}
    units = dict(result.array_units)
    axes = {}
    for name, value in result.axes.items():
        axis = np.asarray(value)
        key = f"axis_{name}"
        if key in arrays:
            raise ValueError(f"structured array {key!r} collides with a stored axis")
        arrays[key] = axis
        units[key] = result.axis_units[name]
        axes[name] = {"array_key": key, "unit": units[key], "length": int(axis.size)}
    return arrays, units, axes


def _serialize_structured_result(result: AlgorithmStructuredResult, algorithm_id: str, label: str) -> tuple[dict[str, object], dict[str, np.ndarray]]:
    """Keep structured axes/arrays in the artifact boundary, not JSON."""
    evidence = _public_evidence(result.evidence)
    arrays, array_units, axis_metadata = _structured_artifact_arrays(result)
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    return {
        "output": {"id": algorithm_id, "label": label, "kind": result.output_kind, "quality": quality},
        "requested_range": result.requested_range,
        "actual_range": result.actual_range,
        "mode": "static",
        "channel_order": result.channel_order,
        "requested_range": result.requested_range,
        "actual_range": result.actual_range,
        "axes": axis_metadata,
        "arrays": {
            name: {"unit": array_units[name], "shape": list(value.shape)}
            for name, value in arrays.items()
        },
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }, arrays


def _serialize_structured_series_result(
    result: AlgorithmStructuredSeriesResult,
    algorithm_id: str,
    label: str,
) -> tuple[dict[str, object], dict[str, np.ndarray]]:
    """Persist dynamic matrices while keeping window evidence out of arrays."""
    evidence = _public_evidence(result.evidence)
    arrays, array_units, axis_metadata = _structured_artifact_arrays(result)
    arrays["window_evidence_json"] = np.asarray(json.dumps(
        [window.evidence for window in result.windows], ensure_ascii=False, sort_keys=True,
    ))
    array_units["window_evidence_json"] = "json"
    quality = {"status": result.quality, "reasons": [result.failure.code] if result.failure else []}
    state_counts: dict[str, int] = {}
    for window in result.windows:
        state_counts[window.state] = state_counts.get(window.state, 0) + 1
    return {
        "output": {"id": algorithm_id, "label": label, "kind": result.output_kind, "mode": "dynamic", "quality": quality},
        "channel_order": result.channel_order,
        "requested_range": result.requested_range,
        "actual_range": result.actual_range,
        "axes": axis_metadata,
        "arrays": {name: {"unit": array_units[name], "shape": list(value.shape)} for name, value in arrays.items()},
        "windows": [window.model_dump(mode="json", exclude={"evidence"}) for window in result.windows],
        "window_state_counts": state_counts,
        "source_quality": evidence["source_quality"],
        "spectral_evidence": evidence["spectral_evidence"],
        "calculation_trace": evidence["calculation_trace"],
        "official": {"algorithm_id": algorithm_id, **evidence},
        "quality": quality,
        "failure": result.failure.model_dump(mode="json") if result.failure else None,
    }, arrays




def _serialize_algorithm_series(
    result: AlgorithmSeriesResult, config: OfficialAlgorithmRunConfig, label: str,
) -> tuple[dict[str, object], dict[str, np.ndarray], str]:
    """Serialize scalar series with the current public point/evidence contract."""
    points = []
    values = []
    for index, (value, time_s, window, quality, failure) in enumerate(zip(result.values, result.time_s, result.windows, result.quality, result.failures, strict=True)):
        evidence = _public_evidence(result.point_evidence[index])
        analysis_state = result.states[index]
        point = {"result_contract_version": DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
                 "time_s": time_s, "window_start_s": window["start_s"], "window_end_s": window["end_s"],
                 "quality": {"status": quality, "reasons": [failure.code] if failure else []},
                 "output": {"id": config.algorithm_id, "label": label, "value": value, "unit": result.unit, "quality": {"status": quality, "reasons": [failure.code] if failure else []}},
                 "channel": result.channel, "source_quality": evidence.get("source_quality", {}),
                 "spectral_evidence": evidence.get("spectral_evidence", {}), "analysis_state": analysis_state,
                 "failure": failure.model_dump(mode="json") if failure else None,
                 "calculation_trace": evidence.get("calculation_trace", {}),
                 "official": {"algorithm_id": config.algorithm_id, **evidence}, "chart": {"kind": "none"}}
        if index < len(result.output_values):
            point["band_values"] = result.output_values[index]
        points.append(point)
        values.append(np.nan if value is None else float(value))
    first = points[0] if points else {"output": {"id": config.algorithm_id, "label": label, "value": None, "unit": result.unit}, "channel": result.channel}
    latest_evidence = _public_evidence(result.point_evidence[-1]) if result.point_evidence else _public_evidence({})
    metric = {
        "result_contract_version": DYNAMIC_ANALYSIS_RESULT_CONTRACT_VERSION,
        "mode": "dynamic", "output": first["output"], "channel": result.channel,
        "actual_range": {"start_s": config.time.start_s, "end_s": config.time.end_s},
        "dynamic_contract": {"window_s": config.dynamic_window_s,
                             "step_s": config.refresh_step_s, "alignment": "window_end"},
        "series": points, "source_quality": latest_evidence.get("source_quality", {}),
        "spectral_evidence": latest_evidence.get("spectral_evidence", {}),
        "official": {"algorithm_id": config.algorithm_id},
        "chart": {"kind": "metric_trend",
                  "x_axis": {"label": "时间", "unit": "s", "field": "time_s"},
                  "y_axis": {"label": label, "unit": result.unit}},
    }
    arrays = {"metric_time_s": np.asarray(result.time_s, dtype=float),
              "metric_values": np.asarray(values, dtype=float)}
    return {"metric": metric}, arrays, result.unit

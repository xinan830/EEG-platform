"""Build additive, backend-authored evidence views for persisted analysis runs."""

from __future__ import annotations

from typing import Any

from app.models.run import AnalysisRun


CONTRACT_VERSION = "analysis-provenance-v1"


def _as_dict(value: object) -> dict[str, Any]:
    return dict(value) if isinstance(value, dict) else {}


def _metric_context(run: AnalysisRun) -> tuple[dict[str, Any], dict[str, Any]]:
    summary = _as_dict(run.result_summary)
    metric = _as_dict(summary.get("metric"))
    if metric.get("mode") != "dynamic":
        return metric, metric
    series = metric.get("series")
    if not isinstance(series, list) or not series:
        return metric, metric
    latest = next((item for item in reversed(series) if isinstance(item, dict)), {})
    return metric, _as_dict(latest)


def _range(value: object) -> dict[str, float] | None:
    payload = _as_dict(value)
    try:
        return {"start_s": float(payload["start_s"]), "end_s": float(payload["end_s"])}
    except (KeyError, TypeError, ValueError):
        return None


def _welch(evidence: dict[str, Any]) -> dict[str, object] | None:
    contract = _as_dict(evidence.get("welch_contract"))
    try:
        return {
            "segment_s": float(contract["welch_segment_s"]),
            "window": str(contract["welch_window"]),
            "overlap_fraction": float(contract["welch_segment_overlap"]),
            "step_s": float(contract["welch_step_s"]),
        }
    except (KeyError, TypeError, ValueError):
        return None


def _frequency(evidence: dict[str, Any]) -> dict[str, object] | None:
    values = evidence.get("frequencies_hz")
    if not isinstance(values, list) or not values:
        return None
    try:
        frequencies = [float(value) for value in values]
    except (TypeError, ValueError):
        return None
    return {"low_hz": frequencies[0], "high_hz": frequencies[-1], "point_count": len(frequencies)}


def _extensions(metric: dict[str, Any], point: dict[str, Any], evidence: dict[str, Any]) -> list[dict[str, object]]:
    extensions: list[dict[str, object]] = []
    band_power = evidence.get("band_power")
    relative = evidence.get("relative_band_power")
    if isinstance(band_power, dict) and isinstance(relative, dict):
        extensions.append({
            "kind": "spectral_band_power",
            "data": {"band_power": band_power, "relative_band_power": relative, "unit": "uV^2"},
        })

    inputs = point.get("inputs", metric.get("inputs"))
    output = _as_dict(metric.get("output"))
    if isinstance(inputs, dict) and output:
        if "value" in point:
            output = {**output, "value": point.get("value"), "quality": point.get("quality")}
        extensions.append({"kind": "metric_inputs_output", "data": {"inputs": inputs, "output": output}})
    trace = point.get("calculation_trace", metric.get("calculation_trace"))
    if isinstance(trace, dict) and isinstance(trace.get("inputs"), list):
        extensions.append({"kind": "algorithm_calculation", "data": trace})
    return extensions


def build_analysis_provenance(run: AnalysisRun) -> dict[str, object]:
    """Project persisted run/result evidence without calculating scientific values."""
    summary = _as_dict(run.result_summary)
    metric, point = _metric_context(run)
    evidence = _as_dict(point.get("spectral_evidence") or metric.get("spectral_evidence") or summary)
    mode = str(metric.get("mode") or run.config.get("mode") or "static")
    actual_range = _range({"start_s": point.get("window_start_s"), "end_s": point.get("window_end_s")})
    actual_range = actual_range or _range(metric.get("actual_range")) or _range(run.actual_range)
    quality = point.get("source_quality") or metric.get("source_quality") or evidence.get("quality")
    quality_payload = _as_dict(quality) or None
    channel = point.get("channel") or metric.get("channel") or run.config.get("channel")

    return {
        "contract_version": CONTRACT_VERSION,
        "status": run.status.value,
        "analysis_type": run.analysis_type,
        "definition_version": run.definition_version,
        "scientific_algorithm_version": evidence.get("algorithm_version", run.scientific_version),
        "implementation_version": run.implementation_version,
        "config_sha256": run.config_sha256,
        "mode": mode,
        "requested_range": _range(run.requested_range),
        "actual_range": actual_range,
        "channel": str(channel) if channel is not None else None,
        "channel_mapping": run.channel_mapping,
        "analysis_reference": evidence.get("analysis_reference", run.reference),
        "sfreq_hz": evidence.get("sfreq_hz"),
        "filter": evidence.get("filter_contract", run.filters) or None,
        "welch": _welch(evidence),
        "frequency": _frequency(evidence),
        "quality": quality_payload,
        "extensions": _extensions(metric, point, evidence),
    }


def serialize_analysis_run(run: AnalysisRun) -> dict[str, object]:
    """Serialize a Run while preserving its legacy fields and adding evidence."""
    payload = run.model_dump(mode="json")
    payload["analysis_provenance"] = build_analysis_provenance(run)
    return payload

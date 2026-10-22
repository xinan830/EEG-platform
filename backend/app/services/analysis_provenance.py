"""Build additive, backend-authored evidence views for persisted analysis runs."""

from __future__ import annotations

from typing import Any

from app.models.run import AnalysisRun
from app.bootstrap.builtin_registry import build_builtin_registry
from app.algorithm_runtime.offline_execution import OFFLINE_EXECUTION_CONTRACT_VERSION


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
    output = _as_dict(point.get("output"))
    if isinstance(inputs, dict) and output:
        extensions.append({"kind": "metric_inputs_output", "data": {"inputs": inputs, "output": output}})
    trace = point.get("calculation_trace", metric.get("calculation_trace"))
    if isinstance(trace, dict) and isinstance(trace.get("inputs"), list):
        extensions.append({"kind": "algorithm_calculation", "data": trace})
    return extensions


def build_analysis_provenance(run: AnalysisRun) -> dict[str, object]:
    """Project current persisted evidence without calculating scientific values."""
    from app.services.current_algorithm_result import require_current_result

    require_current_result(run)
    summary = _as_dict(run.result_summary)
    metric, point = _metric_context(run)
    evidence = _as_dict(point.get("spectral_evidence") or metric.get("spectral_evidence") or summary)
    official = _as_dict(point.get("official") or metric.get("official"))
    projection = {}
    if run.analysis_type == "official_algorithm":
        module = build_builtin_registry().get_current(run.config["algorithm_id"])
        projector = getattr(module, "project_provenance", None)
        if callable(projector):
            projection = projector(official)
    evidence = {**evidence, **_as_dict(projection.get("evidence"))}
    mode = str(metric.get("mode") or run.config.get("mode") or "static")
    actual_range = _range({"start_s": point.get("window_start_s"), "end_s": point.get("window_end_s")})
    actual_range = actual_range or _range(metric.get("actual_range")) or _range(run.actual_range)
    quality = point.get("source_quality") or metric.get("source_quality") or evidence.get("quality")
    quality_payload = _as_dict(quality) or None
    sample_coordinate = _as_dict(official.get("sample_coordinate")) or None
    channel = point.get("channel") or metric.get("channel") or run.config.get("channel")

    module_fields = _as_dict(projection.get("fields"))
    official_identity = None
    if run.analysis_type == "official_algorithm":
        official_identity = {
            "algorithm_id": run.config.get("algorithm_id"),
            "definition_id": run.definition_id,
            "definition_version": run.definition_version,
            "scientific_version": run.scientific_version,
            "implementation_identity": summary.get("implementation_identity", run.config.get("implementation_identity")),
            "implementation_build": run.implementation_version,
        }
    return {
        "contract_version": CONTRACT_VERSION,
        "status": run.status.value,
        "analysis_type": run.analysis_type,
        "definition_version": run.definition_version,
        "official_algorithm_identity": official_identity,
        "scientific_algorithm_version": run.scientific_version,
        "implementation_version": run.implementation_version,
        "config_sha256": run.config_sha256,
        "execution_contract_version": OFFLINE_EXECUTION_CONTRACT_VERSION,
        "execution_mode": run.execution_mode,
        "execution_provenance": _as_dict(summary.get("execution_provenance")) or None,
        "scientific_cache_key": run.cache_key,
        "mode": mode,
        "requested_range": _range(run.requested_range),
        "actual_range": actual_range,
        "sample_coordinate": sample_coordinate,
        "channel": str(channel) if channel is not None else None,
        "channel_mapping": run.channel_mapping,
        "analysis_reference": evidence.get("analysis_reference", run.reference),
        "sfreq_hz": evidence.get("sfreq_hz"),
        "filter": evidence.get("filter_contract", run.filters) or None,
        "preprocessing": module_fields.get("preprocessing"),
        "method": module_fields.get("method"),
        "welch": _welch(evidence),
        "frequency": module_fields.get("frequency") or _frequency(evidence),
        "quality_label_zh": module_fields.get("quality_label_zh"),
        "quality": quality_payload,
        "extensions": [*_extensions(metric, point, evidence), *projection.get("extensions", [])],
    }


def serialize_analysis_run(run: AnalysisRun) -> dict[str, object]:
    """Serialize the current Run contract and its backend-authored evidence."""
    payload = run.model_dump(mode="json")
    payload["analysis_provenance"] = build_analysis_provenance(run)
    return payload

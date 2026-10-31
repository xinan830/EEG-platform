"""Deterministic TNK child-to-group result publication."""

from __future__ import annotations

from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.scientific.quality import SpectralQualityGateError

from .group_graph import TNK_ROLE_ORDER


def scheduled_child_result(child: Any, start_s: float, end_s: float,
                           code: str, message: str) -> AlgorithmResult:
    roles = list(child.module.manifest.channel_roles)
    schedule_state = {
        "TNK_UPDATE_NOT_DUE": "not_due",
        "TNK_CONTEXT_WARMUP": "warmup",
        "TNK_SEGMENT_REQUIRED": "unsupported",
    }.get(code, "failed")
    return AlgorithmResult(
        value=None, unit=child.module.manifest.output_unit, channel="/".join(roles),
        requested_range={"start_s": start_s, "end_s": end_s},
        actual_range={"start_s": start_s, "end_s": end_s},
        quality="insufficient_data", failure=AlgorithmFailure(code=code, message=message),
        output_values={role: None for role in roles} if child.manifest.algorithm_id in {
            "tnk_hai", "tnk_aperiodic", "tnk_fatigue",
        } else None,
        evidence={"extensions": {"tnk_evidence": {
            "provenance": {"algorithm_id": child.manifest.algorithm_id,
                           "schedule_state": schedule_state},
        }}},
    )


def quality_rejected_result(child: Any, child_config: Any, start_s: float, end_s: float,
                            error: SpectralQualityGateError) -> AlgorithmResult:
    quality = dict(error.quality)
    roles = list(child.module.manifest.channel_roles)
    output_values = {role: None for role in roles} if child.manifest.algorithm_id in {
        "tnk_hai", "tnk_aperiodic", "tnk_fatigue",
    } else None
    return AlgorithmResult(
        value=None, unit=child.module.manifest.output_unit,
        channel="/".join(child.requested_channels(child_config)),
        requested_range={"start_s": start_s, "end_s": end_s},
        actual_range={"start_s": start_s, "end_s": end_s}, quality="gate_failed",
        failure=AlgorithmFailure(
            code="TNK_QUALITY_REJECTED", message="TNK 窗口未通过共享频谱质量门",
            detail={"quality": quality},
        ),
        output_values=output_values,
        evidence={"source_quality": quality, "extensions": {"tnk_evidence": {
            "provenance": {"algorithm_id": child.manifest.algorithm_id},
            "quality_gate": quality,
        }}},
    )


def metric_values(metric_units: dict[str, str], results: dict[str, list[Any]]) -> tuple[dict[str, float | None], dict[str, Any]]:
    values: dict[str, float | None] = {}
    evidence: dict[str, Any] = {}
    for child_id, rows in results.items():
        evidence[child_id] = [row.model_dump(mode="json") for row in rows]
        if child_id in {"tnk_hai", "tnk_aperiodic", "tnk_fatigue"}:
            for row in rows:
                for role, value in (row.output_values or {}).items():
                    values[f"{child_id}.{role}"] = value
        elif child_id == "tnk_rbp":
            for role, row in zip(TNK_ROLE_ORDER, rows, strict=True):
                for band, value in (row.output_values or {}).items():
                    values[f"tnk_rbp.{role}.{band}"] = value
        else:
            values[child_id] = rows[0].value
    return {name: values.get(name) for name in metric_units}, evidence


def quality(results: dict[str, list[Any]]) -> str:
    qualities = [row.quality for rows in results.values() for row in rows]
    if qualities and all(value == "clean" for value in qualities):
        return "clean"
    if "gate_failed" in qualities:
        return "partial"
    if qualities and all(value in {"gate_failed", "unavailable", "insufficient_data"} for value in qualities):
        return "unavailable"
    return "partial"


def analysis_state(group_quality: str) -> str:
    if group_quality == "clean":
        return "Complete"
    if group_quality == "unavailable":
        return "Rejected"
    return "Partial"


def group_failure(group_quality: str) -> AlgorithmFailure | None:
    if group_quality != "unavailable":
        return None
    return AlgorithmFailure(
        code="TNK_GROUP_WINDOW_QUALITY_REJECTED",
        message="该动态窗口的全部 TNK 子指标均未通过输入质量门",
    )


def point_evidence(adapter: Any, config: Any, results: dict[str, Any],
                   values: dict[str, float | None]) -> dict[str, Any]:
    return {"extensions": {"tnk_evidence": {
        "group_id": adapter.manifest.algorithm_id,
        "execution_graph": {
            "version": adapter.group_graph.version,
            "identity": adapter.group_graph.identity,
        },
        "role_mapping": dict(config.role_mapping),
        "children": results,
        "metric_values": values,
    }}}

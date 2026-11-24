"""Prepare TNK frame inputs serially, then execute the declared graph."""

from __future__ import annotations

import logging
import time
from copy import deepcopy
from dataclasses import dataclass
from typing import Any

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult
from app.algorithm_runtime.group_execution import AlgorithmGroupExecutor, AlgorithmGroupNode
from app.scientific.quality import SpectralQualityGateError

from .group_graph import TNK_ROLE_ORDER, dynamic_child_range
from .group_input import child_config, child_inputs_and_window, prewarm_shared_spectra
from .group_results import quality_rejected_result, scheduled_child_result

_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")


def fresh_tnk_children(adapter: Any) -> tuple[Any, ...]:
    """Copy templates as one object graph so shared stream owners stay shared."""
    return deepcopy(adapter.children)


def reset_tnk_children(children: tuple[Any, ...]) -> None:
    seen_bundles: set[int] = set()
    for child in children:
        if hasattr(child.module, "reset"):
            child.module.reset()
        bundle = getattr(child, "stream_bundle", None)
        if bundle is not None and id(bundle) not in seen_bundles:
            bundle.reset()
            seen_bundles.add(id(bundle))


@dataclass(frozen=True)
class _PreparedNode:
    child: Any
    node: AlgorithmGroupNode
    entries: tuple[Any, ...]
    start_s: float
    end_s: float
    input_elapsed_ms: float = 0.0
    calculation_count: int = 0


@dataclass(frozen=True)
class _NodeOutcome:
    results: list[AlgorithmResult]
    calculation_count: int = 0


def _scheduled(child: Any, node: AlgorithmGroupNode,
               config: Any, start_s: float, end_s: float) -> AlgorithmResult:
    elapsed = end_s - config.start_s
    if not node.supports_dynamic:
        code, message = "TNK_SEGMENT_REQUIRED", "TNK FAA 需要完整配对事件段；当前游标动态分析没有事件段输入。"
    elif node.node_id in {"tnk_iapf", "tnk_hai", "tnk_aperiodic"} and elapsed < 30.0 - 1e-9:
        code, message = "TNK_CONTEXT_WARMUP", "该指标需要最近 30 秒的连续数据；当前记录位置尚未满足预热长度。"
    else:
        code, message = "TNK_UPDATE_NOT_DUE", f"该指标当前游标尚未到每 {node.cadence_s:g} 秒的更新节拍。"
    return scheduled_child_result(child, start_s, end_s, code, message)


def _dependency_failed(child: Any, node: AlgorithmGroupNode,
                       dependencies: tuple[str, ...], start_s: float, end_s: float) -> _NodeOutcome:
    row = scheduled_child_result(
        child, start_s, end_s, "TNK_DEPENDENCY_FAILED",
        "TNK 子指标的必需依赖未能提供有效结果。",
    )
    row.quality = "unavailable"
    row.failure = AlgorithmFailure(
        code="TNK_DEPENDENCY_FAILED", message="TNK 子指标的必需依赖未能提供有效结果。",
        detail={"node_id": node.node_id, "dependencies": list(dependencies)},
    )
    return _NodeOutcome([row])


def _dependency_result(outcomes: dict[str, Any], node_id: str) -> AlgorithmResult | None:
    outcome = outcomes.get(node_id)
    return outcome.results[0] if outcome is not None and outcome.results else None


def _legacy_stream_failure(
    child: Any, child_config: Any, start_s: float, end_s: float,
    *, code: str, message: str,
) -> AlgorithmResult:
    return AlgorithmResult(
        value=None, unit=child.module.manifest.output_unit,
        channel="/".join(child.requested_channels(child_config)),
        requested_range={"start_s": start_s, "end_s": end_s},
        actual_range={"start_s": start_s, "end_s": end_s}, quality="unavailable",
        failure=AlgorithmFailure(code=code, message=message),
        evidence={"extensions": {"tnk_evidence": {
            "provenance": {"algorithm_id": child.manifest.algorithm_id, "failure_stage": "legacy_stream"},
        }}},
    )


def _add_dependency_context(child_id: str, window: Any, outcomes: dict[str, Any]) -> None:
    if child_id == "tnk_composite_meditation":
        spatial = _dependency_result(outcomes, "tnk_spatial_distribution")
        stability = _dependency_result(outcomes, "tnk_rhythm_stability")
        window.provenance.update({
            "spatial_distribution": spatial.value if spatial else None,
            "rhythm_stability": stability.value if stability else None,
            "spatial_valid": bool(spatial and spatial.quality == "clean"),
            "stability_ready": bool(stability and stability.quality == "clean"),
        })
    elif child_id == "tnk_brainbeat_flat":
        aperiodic = _dependency_result(outcomes, "tnk_aperiodic")
        if aperiodic is None:
            return
        fit_evidence = aperiodic.evidence.get("extensions", {}).get("tnk_evidence", {})
        exponents = aperiodic.output_values or {}
        offsets = fit_evidence.get("offsets", {})
        fit_quality = fit_evidence.get("fit_quality", {})
        window.provenance["aperiodic_fit_by_role"] = {
            role: {
                "offset": offsets[role], "exponent": exponent,
                "fit_start_s": aperiodic.actual_range["start_s"],
                "fit_end_s": aperiodic.actual_range["end_s"],
                "fit_quality": fit_quality.get(role),
            }
            for role, exponent in exponents.items()
            if exponent is not None and role in offsets
        }


def execute_tnk_frame(
    adapter: Any,
    executor: AlgorithmGroupExecutor,
    inputs: Any,
    config: Any,
    start_s: float,
    end_s: float,
    *,
    children: tuple[Any, ...],
    diagnostics: dict[str, int] | None = None,
) -> dict[str, list[AlgorithmResult]]:
    """Run one ordered frame; no reader or shared cache is used by worker actions."""
    child_by_id = {child.manifest.algorithm_id: child for child in children}
    ranges_by_id = {
        node.node_id: dynamic_child_range(node, config.start_s, end_s)
        for node in adapter.group_graph.nodes
    }
    spectrum_ranges = {
        child_range for node in adapter.group_graph.nodes
        if node.input_representation in {"spectrum", "spectrum_with_dependencies"}
        if (child_range := ranges_by_id[node.node_id]) is not None
    }
    shared_spectra = prewarm_shared_spectra(inputs, config, spectrum_ranges, diagnostics)

    legacy_children = [child_by_id[node.node_id] for node in adapter.group_graph.nodes
                       if getattr(child_by_id[node.node_id], "stream_bundle", None) is not None]
    due_legacy = [child for child in legacy_children if ranges_by_id[child.manifest.algorithm_id] is not None]
    legacy_outputs: dict[str, AlgorithmResult] = {}
    legacy_failure: Exception | None = None
    if due_legacy:
        bundles = {id(child.stream_bundle): child.stream_bundle for child in due_legacy}
        if len(bundles) != 1:
            raise ValueError("TNK legacy children must use one shared stream bundle")
        started = time.perf_counter()
        if not hasattr(inputs.payload, "load_time_series_source"):
            legacy_failure = AttributeError("TNK 共享时序数据访问能力不可用")
        else:
            try:
                legacy_outputs = next(iter(bundles.values())).advance(
                    inputs, channel=inputs.channel,
                    end_sample=int(round(end_s * inputs.sfreq_hz)), start_s=start_s, end_s=end_s,
                )
                if diagnostics is not None:
                    diagnostics["shared_time_series_loads"] = diagnostics.get("shared_time_series_loads", 0) + 1
            except (AttributeError, KeyError, ValueError) as exc:
                legacy_failure = exc
        _LOGGER.info(
            "tnk_legacy_stream_prepare_perf end_sample=%s output_count=%s state=%s elapsed_ms=%.1f",
            int(round(end_s * inputs.sfreq_hz)), len(legacy_outputs),
            "failed" if legacy_failure is not None else "ready",
            (time.perf_counter() - started) * 1000,
        )

    prepared: dict[str, _PreparedNode] = {}
    spectrum_cache: dict[tuple[Any, ...], Any] = {}
    for node in adapter.group_graph.nodes:
        child = child_by_id[node.node_id]
        child_range = ranges_by_id[node.node_id]
        if child_range is None:
            prepared[node.node_id] = _PreparedNode(
                child, node, (_scheduled(child, node, config, start_s, end_s),), start_s, end_s,
            )
            continue
        child_start_s, child_end_s = child_range
        if node.input_representation == "time_series":
            child_cfg = child_config(child, config, child_start_s, child_end_s)
            result = legacy_outputs.get(node.node_id)
            if result is None:
                if legacy_failure is not None:
                    result = _legacy_stream_failure(
                        child, child_cfg, child_start_s, child_end_s,
                        code="TNK_LEGACY_INPUT_INVALID", message=str(legacy_failure),
                    )
                else:
                    result = _legacy_stream_failure(
                        child, child_cfg, child_start_s, child_end_s,
                        code="TNK_LEGACY_STREAM_OUTPUT_MISSING",
                        message=f"TNK 共享时序流未返回子指标 {node.node_id} 的结果。",
                    )
            result.channel = "/".join(child.requested_channels(child_cfg))
            result.requested_range = {"start_s": child_start_s, "end_s": child_end_s}
            result.actual_range = {"start_s": child_start_s, "end_s": child_end_s}
            prepared[node.node_id] = _PreparedNode(child, node, (result,), child_start_s, child_end_s)
            continue

        roles = TNK_ROLE_ORDER if node.node_id == "tnk_rbp" else ("Fz",)
        entries: list[Any] = []
        input_elapsed_ms = 0.0
        for role in roles:
            child_cfg = child_config(child, config, child_start_s, child_end_s, primary_role=role)
            input_started = time.perf_counter()
            try:
                child_inputs, window = child_inputs_and_window(
                    inputs, child, child_cfg, child_start_s, child_end_s,
                    spectrum_cache, diagnostics, shared_spectra,
                )
            except SpectralQualityGateError as exc:
                entries.append(quality_rejected_result(child, child_cfg, child_start_s, child_end_s, exc))
            else:
                entries.append((child_inputs, child_cfg, window))
            input_elapsed_ms += (time.perf_counter() - input_started) * 1000
        calculation_count = sum(not isinstance(entry, AlgorithmResult) for entry in entries)
        prepared[node.node_id] = _PreparedNode(
            child, node, tuple(entries), child_start_s, child_end_s,
            input_elapsed_ms, calculation_count,
        )

    def action_for(item: _PreparedNode):
        def run(outcomes: dict[str, Any]) -> _NodeOutcome:
            rows: list[AlgorithmResult] = []
            compute_elapsed_ms = 0.0
            for entry in item.entries:
                if isinstance(entry, AlgorithmResult):
                    rows.append(entry)
                    continue
                child_inputs, child_cfg, window = entry
                _add_dependency_context(item.node.node_id, window, outcomes)
                started = time.perf_counter()
                result = item.child.module.compute(
                    window, item.child._tnk_config(child_cfg, child_inputs.sfreq_hz),
                )
                compute_elapsed_ms += (time.perf_counter() - started) * 1000
                result.channel = "/".join(item.child.requested_channels(child_cfg))
                result.requested_range = {"start_s": item.start_s, "end_s": item.end_s}
                result.actual_range = dict(result.requested_range)
                rows.append(result)
            if rows and item.node.input_representation != "time_series":
                _LOGGER.info(
                    "tnk_child_window_perf child_id=%s roles=%s input_ms=%.1f compute_ms=%.1f",
                    item.node.node_id, item.calculation_count, item.input_elapsed_ms, compute_elapsed_ms,
                )
            return _NodeOutcome(rows, item.calculation_count)
        return run

    def is_failure(outcome: _NodeOutcome) -> bool:
        return any(row.failure is not None for row in outcome.results)

    actions = {node_id: action_for(item) for node_id, item in prepared.items()}
    outcomes = executor.execute(
        actions,
        is_failure=is_failure,
        dependency_failure=lambda node, failed: _dependency_failed(
            child_by_id[node.node_id], node, failed, start_s, end_s,
        ),
    )
    results = {node_id: outcome.results for node_id, outcome in outcomes.items()}
    if diagnostics is not None:
        diagnostics["child_calculations"] = diagnostics.get("child_calculations", 0) + sum(
            outcome.calculation_count for outcome in outcomes.values()
        )
    return results

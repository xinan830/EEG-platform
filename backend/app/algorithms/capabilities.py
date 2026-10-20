"""Construct explicit official dynamic strategy/reuse declarations."""

from app.algorithm_runtime.contracts import CapabilityEvidenceStatus, DynamicStrategyCapabilities
from app.algorithms.rbp.official import RBP_BANDS


def official_dynamic_capabilities(*, full_to_preview: bool, on_demand_to_full: bool = False) -> DynamicStrategyCapabilities:
    """Declare shared lifecycle support while retaining per-module reuse choices.

    Evidence references are deliberately attached only to paths with existing
    recorded validation. Combined stress and inverse reuse remain unvalidated
    until their own algorithm-specific evidence is added.
    """
    full = CapabilityEvidenceStatus(
        implementation="implemented", validation="validated",
        evidence=["backend/tests/test_official_algorithm_runs.py"],
    )
    on_demand = CapabilityEvidenceStatus(
        implementation="implemented", validation="validated",
        evidence=["docs/algorithms/on-demand-dynamic-capability-matrix.md"],
    )
    combined = CapabilityEvidenceStatus(
        implementation="implemented", validation="unvalidated",
        reason="Combined-path evidence is not yet complete for this algorithm.",
    )
    if full_to_preview:
        forward = CapabilityEvidenceStatus(
            implementation="implemented", validation="validated",
            evidence=["docs/algorithms/on-demand-dynamic-capability-matrix.md"],
        )
    else:
        forward = CapabilityEvidenceStatus(
            implementation="unsupported", validation="not_applicable",
            reason="The algorithm does not declare a safe full-result projection contract.",
        )
    if on_demand_to_full:
        inverse = CapabilityEvidenceStatus(
            implementation="implemented", validation="unvalidated",
            reason="The module provides an algorithm-specific exact-window reuse validator; directional validation evidence is pending.",
        )
    else:
        inverse = CapabilityEvidenceStatus(
            implementation="unsupported", validation="not_applicable",
            reason="The algorithm does not provide an on-demand window reuse validator for full Runs.",
        )
    return DynamicStrategyCapabilities(
        full=full,
        on_demand=on_demand,
        combined_preview_background_full=combined,
        full_result_to_preview=forward,
        on_demand_result_to_full=inverse,
    )


def rbp_definition_graph() -> dict[str, object]:
    nodes: list[dict[str, object]] = []
    for band, low, high in RBP_BANDS:
        nodes.append({
            "id": f"{band}_power", "type": "band_power",
            "inputs": {"source": "$input.psd"},
            "parameters": {"low_hz": low, "high_hz": high},
        })
    total_id = "delta_power"
    for band, *_ in RBP_BANDS[1:]:
        next_id = f"total_through_{band}"
        nodes.append({
            "id": next_id, "type": "add",
            "inputs": {"left": total_id, "right": f"{band}_power"},
            "parameters": {},
        })
        total_id = next_id
    outputs: list[str] = []
    for band, *_ in RBP_BANDS:
        output_id = f"{band}_rbp"
        nodes.append({
            "id": output_id, "type": "relative_band_power",
            "inputs": {"numerator": f"{band}_power", "denominator": total_id},
            "parameters": {},
        })
        outputs.append(output_id)
    return {"nodes": nodes, "outputs": outputs}


def official_result_graph(field_names: list[str]) -> dict[str, object]:
    """Persist a descriptor graph that delegates science to the official module."""
    nodes = [
        {"id": field, "type": "output", "inputs": {"source": f"$input.official_result.{field}"}, "parameters": {}}
        for field in field_names
    ]
    return {"nodes": nodes, "outputs": field_names}

"""Construct explicit official dynamic strategy/reuse declarations."""

from app.algorithm_runtime.contracts import (
    AlgorithmManifest,
    AlgorithmOutputContract,
    AlgorithmOutputField,
    CapabilityEvidenceStatus,
    DynamicStrategyCapabilities,
)
from app.algorithms.rbp.official import RBP_BANDS


def declared_output_contract(
    manifest: AlgorithmManifest,
    *,
    static_result_type: str,
    dynamic_result_type: str,
    evidence_fields: list[str],
) -> AlgorithmOutputContract:
    """Build a typed declaration from module-owned output and signal metadata."""
    requirements = manifest.signal_requirements
    if requirements is None:
        raise ValueError(f"{manifest.algorithm_id} must declare signal roles before output contract")
    schema = manifest.output_schema
    window_shapes = schema.get("window_shapes", {})
    fields = [
        AlgorithmOutputField(
            name=item["name"], unit=item["unit"], meaning=item["meaning"],
            shape=list(window_shapes[item["name"]]),
        )
        for item in schema["fields"]
    ]
    return AlgorithmOutputContract(
        static_result_type=static_result_type,
        dynamic_result_type=dynamic_result_type,
        channel_role_order=list(requirements.input_roles),
        fields=fields,
        axes=list(schema.get("window_axes", [])),
        quality_values=["clean", "gate_failed", "unavailable", "insufficient_data", "partial"],
        dynamic_states=["Partial", "Complete", "Rejected", "Unavailable"],
        failure_semantics=(
            "Failed, rejected, or unavailable outputs carry null values or unavailable array rows "
            "and an explicit failure; zero is never substituted."
        ),
        evidence_fields=["source_quality", "spectral_evidence", "calculation_trace", *evidence_fields],
    )


def official_dynamic_capabilities(
    *, full_to_preview: bool, on_demand_to_full: bool = False,
    combined_evidence: list[str] | None = None, inverse_reuse_evidence: list[str] | None = None,
) -> DynamicStrategyCapabilities:
    """Declare shared lifecycle support while retaining per-module reuse choices.

    New modules remain unvalidated until they explicitly supply evidence for
    combined execution and inverse reuse independently.
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
        implementation="implemented", validation="validated" if combined_evidence else "unvalidated",
        evidence=combined_evidence or [],
        reason=None if combined_evidence else "Combined-path evidence is not yet complete for this algorithm.",
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
            implementation="implemented", validation="validated" if inverse_reuse_evidence else "unvalidated",
            evidence=inverse_reuse_evidence or [],
            reason=None if inverse_reuse_evidence else "The module provides an algorithm-specific exact-window reuse validator; directional validation evidence is pending.",
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


def tnk_dynamic_capabilities(*, segment_only: bool = False) -> DynamicStrategyCapabilities:
    """Declare the strategy surface after deterministic parity acceptance."""
    validated_reason = "TNK deterministic parity evidence is recorded in docs/validation/tnk-strategy-parity.json."
    full = CapabilityEvidenceStatus(
        implementation="implemented", validation="validated",
        evidence=["docs/validation/tnk-strategy-parity.json"], reason=validated_reason,
    )
    if segment_only:
        on_demand = CapabilityEvidenceStatus(
            implementation="unsupported", validation="not_applicable",
            reason="FAA requires a complete paired segment/report, not an arbitrary cursor window.",
        )
    else:
        on_demand = CapabilityEvidenceStatus(
            implementation="implemented", validation="validated",
            evidence=["docs/validation/tnk-strategy-parity.json"], reason=validated_reason,
        )
    combined = CapabilityEvidenceStatus(
        implementation="implemented", validation="validated",
        evidence=["docs/validation/tnk-strategy-parity.json"], reason=validated_reason,
    )
    unsupported_reuse = CapabilityEvidenceStatus(
        implementation="unsupported", validation="not_applicable",
        reason="TNK reuse direction is unavailable until exact context parity is accepted.",
    )
    if segment_only:
        unsupported_reuse = CapabilityEvidenceStatus(
            implementation="unsupported", validation="not_applicable",
            reason="FAA cannot project a complete segment report to an arbitrary cursor window.",
        )
    return DynamicStrategyCapabilities(
        full=full,
        on_demand=on_demand,
        combined_preview_background_full=combined,
        full_result_to_preview=unsupported_reuse,
        on_demand_result_to_full=unsupported_reuse,
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

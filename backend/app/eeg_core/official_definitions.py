"""Frozen metadata for official algorithms during shadow-only migration."""

from __future__ import annotations

from app.eeg_core.analysis_contract import ANALYSIS_ALGORITHM_VERSION, ANALYSIS_CONTRACT


OFFICIAL_DEFINITIONS: dict[str, dict[str, object]] = {
    "rbp": {"version": "1.0.0", "implementation": ANALYSIS_ALGORITHM_VERSION, "inputs": ["PSD"],
            "formula": "band_power / band_power_1_30", "bands": ANALYSIS_CONTRACT["frequency_band_edges"],
            "quality": ANALYSIS_CONTRACT["quality_gate_policy"]},
    "faa": {"version": "1.0.0", "implementation": "faa-legacy-v1", "inputs": ["F3", "F4"],
            "formula": "ln(alpha_power_F4) - ln(alpha_power_F3)", "epoch_s": 2.0, "overlap": 0.5,
            "paired_quality": True, "minimum_clean_epochs": 10},
    "brainbeat": {"version": "1.0.0", "implementation": "realtime-eegprocessor-v1", "inputs": ["Fz", "Pz", "IAPF"],
                  "formula": "relative_theta_Fz / relative_alpha_Pz", "welch_segment_s": 2.0,
                  "overlap": 0.5, "stateful_ema": True},
    "theta_beta": {"version": "1.0.0", "implementation": ANALYSIS_ALGORITHM_VERSION, "inputs": ["Fz", "Pz", "Oz", "IAPF"],
                    "formula": "theta(iapf-6..iapf-2) / beta(iapf+2..30)", "channels": ["Fz", "Pz", "Oz"],
                    "quality": ANALYSIS_CONTRACT["quality_gate_policy"]},
    "iapf": {"version": "1.0.0", "implementation": ANALYSIS_ALGORITHM_VERSION, "inputs": ["PSD"],
             "fit": ANALYSIS_CONTRACT["aperiodic_model"], "search_hz": ANALYSIS_CONTRACT["iapf_search_hz"],
             "sources": ["peak", "cog"], "lock_candidates": ANALYSIS_CONTRACT["iapf_lock_candidates"]},
}


def official_definition_draft(name: str):
    """Return the immutable v1 definition record for an official metric.

    FAA, BrainBeat and IAPF deliberately use an ``official_result`` boundary:
    the current generic graph runner cannot represent their paired epochs,
    realtime EMA, or 1/f peak/COG decision without changing semantics.  They
    remain explicitly marked as composite until a later executor capability
    can represent those contracts faithfully.
    """
    from app.models.algorithm_definition import DefinitionVersionDraft

    metadata = official_definition(name)
    if name == "rbp":
        nodes: list[dict[str, object]] = []
        outputs: list[str] = []
        for band, low, high in ("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0):
            power_id, output_id = f"{band}_power", f"{band}_rbp"
            nodes.extend((
                {"id": power_id, "type": "band_power", "inputs": {"source": "$input.psd"}, "parameters": {"low_hz": low, "high_hz": high}},
                {"id": output_id, "type": "relative_band_power", "inputs": {"numerator": power_id, "denominator": "total_power"}, "parameters": {}},
            ))
            outputs.append(output_id)
        nodes.insert(0, {"id": "total_power", "type": "band_power", "inputs": {"source": "$input.psd"}, "parameters": {"low_hz": 1.0, "high_hz": 30.0}})
        graph = {"nodes": nodes, "outputs": outputs}
        inputs = {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}}
        output_contract = {band: {"type": "RelativePower", "unit": "ratio"} for band in ("delta", "theta", "alpha", "beta")}
        execution_kind = "generic_research_primitives"
    else:
        graph = {"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.official_result"}, "parameters": {}}], "outputs": ["out"]}
        inputs = {"official_result": {"type": "Scalar", "unit": "dimensionless_or_declared_output", "source": "official_composite_adapter"}}
        output_contract = {"out": {"type": "Scalar", "unit": "Hz" if name == "iapf" else "ratio_or_dimensionless"}}
        execution_kind = "official_composite_shadow_only"
    return DefinitionVersionDraft(
        semver=str(metadata["version"]), graph=graph, inputs=inputs,
        outputs=output_contract, units={"input": inputs, "output": output_contract},
        quality_rules={"contract": metadata.get("quality", metadata), "execution_kind": execution_kind},
        references=["docs/architecture/official-algorithm-migration.md"],
    )


def ensure_official_definitions(service) -> dict[str, str]:
    """Idempotently persist published, immutable official v1 definitions.

    This creates definition records only.  It does not route any API result
    through the new executor; migration remains shadow-only until its evidence
    is complete and a later cutover change is approved.
    """
    from app.models.algorithm_definition import DefinitionCreateRequest

    existing = {(item.name, item.owner): item for item in service.list()}
    persisted: dict[str, str] = {}
    for name in OFFICIAL_DEFINITIONS:
        title = f"Official {name.upper()}"
        definition = existing.get((title, "platform-official"))
        if definition is None:
            definition = service.create(DefinitionCreateRequest(name=title, owner="platform-official", description=f"Frozen official {name} contract; shadow migration only."))
        draft = official_definition_draft(name)
        version = service.repository.get_version(definition.definition_id, draft.semver)
        if version is None:
            version = service.create_version(definition.definition_id, draft)
        if version.state != "published":
            version = service.publish(definition.definition_id, draft.semver)
        persisted[name] = version.digest_sha256
    return persisted


def official_definition(name: str) -> dict[str, object]:
    """Return a shallow immutable-by-convention snapshot for one official algorithm."""
    try:
        return dict(OFFICIAL_DEFINITIONS[name])
    except KeyError as exc:
        raise ValueError(f"unknown official algorithm: {name}") from exc

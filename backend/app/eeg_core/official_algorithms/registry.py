"""Single source of truth for official algorithm identity and lifecycle."""

from __future__ import annotations

from typing import Any

from app.eeg_core.analysis_contract import ANALYSIS_ALGORITHM_VERSION, ANALYSIS_CONTRACT, LIVE_ANALYSIS_CONTRACT
from app.eeg_core.official_algorithms.contracts import OfficialAlgorithmCatalogItem, OfficialAlgorithmManifest


OFFICIAL_ALGORITHM_MANIFESTS: tuple[OfficialAlgorithmManifest, ...] = (
    OfficialAlgorithmManifest(
        algorithm_id="rbp", definition_name="Official RBP", display_name_zh="相对频段功率", abbreviation="RBP",
        purpose_zh="展示 Delta、Theta、Alpha、Beta 在 1–30 Hz 总功率中的相对占比。",
        scientific_version=ANALYSIS_ALGORITHM_VERSION, implementation_identity=ANALYSIS_ALGORITHM_VERSION,
        execution_kind="generic_research_primitives",
    ),
    OfficialAlgorithmManifest(
        algorithm_id="theta_beta", definition_name="Official THETA_BETA", display_name_zh="Theta/Beta 比值", abbreviation="Theta/Beta",
        purpose_zh="根据同一选定原始通道的个体 Alpha 峰，计算 Theta 与 Beta 频段比值。",
        scientific_version="official-theta-beta-v2", implementation_identity="theta-beta-runtime-v1",
        execution_kind="official_composite_run_adapter", availability="available", is_runnable=True,
        required_channel_roles=["Fz", "Pz", "Oz"], supported_modes=["static", "dynamic"],
    ),
    OfficialAlgorithmManifest(
        algorithm_id="faa", definition_name="Official FAA", display_name_zh="额叶 Alpha 不对称性", abbreviation="FAA",
        purpose_zh="比较 F3 与 F4 的 Alpha 功率对数差；使用成对质量门。",
        scientific_version=ANALYSIS_ALGORITHM_VERSION, implementation_identity="faa-legacy-v1",
        execution_kind="official_composite_shadow_only", required_channel_roles=["F3", "F4"],
    ),
    OfficialAlgorithmManifest(
        algorithm_id="brainbeat", definition_name="Official BRAINBEAT", display_name_zh="脑节律指标", abbreviation="BrainBeat",
        purpose_zh="实时链路中的前额 Theta 与顶区 Alpha 相对功率关系；含状态性 EMA warm-up。",
        scientific_version=LIVE_ANALYSIS_CONTRACT["algorithm_version"], implementation_identity="realtime-eegprocessor-v1",
        execution_kind="official_composite_shadow_only", required_channel_roles=["Fz", "Pz", "IAPF"],
    ),
    OfficialAlgorithmManifest(
        algorithm_id="iapf", definition_name="Official IAPF", display_name_zh="个体 Alpha 峰频", abbreviation="IAPF",
        purpose_zh="使用 1/f 拟合后的 Alpha 残差 Peak/COG 估计个体 Alpha 峰频。",
        scientific_version="official-iapf-v2", implementation_identity="iapf-runtime-v3",
        execution_kind="official_composite_run_adapter", availability="available", is_runnable=True,
        supported_modes=["static", "dynamic"],
    ),
)


def _manifest(algorithm_id: str) -> OfficialAlgorithmManifest:
    for item in OFFICIAL_ALGORITHM_MANIFESTS:
        if item.algorithm_id == algorithm_id:
            return item
    raise ValueError(f"unknown official algorithm: {algorithm_id}")


def official_definition(algorithm_id: str) -> dict[str, object]:
    """Compatibility metadata for legacy inspectors and shadow tooling."""
    manifest = _manifest(algorithm_id)
    values: dict[str, object] = {
        "version": "1.0.0", "implementation": manifest.implementation_identity,
        "execution_kind": manifest.execution_kind, "availability": manifest.availability,
    }
    if algorithm_id == "rbp":
        values.update({"inputs": ["PSD"], "formula": "band_power / band_power_1_30", "bands": ANALYSIS_CONTRACT["frequency_band_edges"], "quality": ANALYSIS_CONTRACT["quality_gate_policy"]})
    elif algorithm_id == "faa":
        values.update({"inputs": ["F3", "F4"], "formula": "ln(alpha_power_F4) - ln(alpha_power_F3)", "epoch_s": 2.0, "overlap": 0.5, "paired_quality": True, "minimum_clean_epochs": 10})
    elif algorithm_id == "brainbeat":
        values.update({"inputs": ["Fz", "Pz", "IAPF"], "formula": "relative_theta_Fz / relative_alpha_Pz", "welch_segment_s": 2.0, "overlap": 0.5, "stateful_ema": True})
    elif algorithm_id == "theta_beta":
        values.update({"inputs": ["selected_raw_channel", "IAPF"], "formula": "theta(iapf-6..iapf-2) / beta(iapf+2..30)", "channels": ["selected_raw_channel"], "quality": ANALYSIS_CONTRACT["quality_gate_policy"]})
    elif algorithm_id == "iapf":
        values.update({"inputs": ["PSD"], "fit": ANALYSIS_CONTRACT["aperiodic_model"], "search_hz": ANALYSIS_CONTRACT["iapf_search_hz"], "sources": ["peak", "cog"], "lock_candidates": ANALYSIS_CONTRACT["iapf_lock_candidates"]})
    return values


def official_definition_draft(algorithm_id: str):
    """Return the immutable v1 definition without claiming generic executability."""
    from app.models.algorithm_definition import DefinitionVersionDraft

    metadata = official_definition(algorithm_id)
    if algorithm_id == "rbp":
        nodes: list[dict[str, object]] = []
        outputs: list[str] = []
        for band, low, high in (("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0)):
            power_id, output_id = f"{band}_power", f"{band}_rbp"
            nodes.extend((
                {"id": power_id, "type": "band_power", "inputs": {"source": "$input.psd"}, "parameters": {"low_hz": low, "high_hz": high}},
                {"id": output_id, "type": "relative_band_power", "inputs": {"numerator": power_id, "denominator": "total_power"}, "parameters": {}},
            ))
            outputs.append(output_id)
        nodes.insert(0, {"id": "total_power", "type": "band_power", "inputs": {"source": "$input.psd"}, "parameters": {"low_hz": 1.0, "high_hz": 30.0}})
        graph = {"nodes": nodes, "outputs": outputs}
        inputs: dict[str, Any] = {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}}
        output_contract: dict[str, Any] = {band: {"type": "RelativePower", "unit": "ratio"} for band in ("delta", "theta", "alpha", "beta")}
    else:
        graph = {"nodes": [{"id": "out", "type": "output", "inputs": {"source": "$input.official_result"}, "parameters": {}}], "outputs": ["out"]}
        inputs = {"official_result": {"type": "Scalar", "unit": "dimensionless_or_declared_output", "source": "official_composite_adapter"}}
        output_contract = {"out": {"type": "Scalar", "unit": "Hz" if algorithm_id == "iapf" else "ratio_or_dimensionless"}}
    return DefinitionVersionDraft(
        semver=str(metadata["version"]), graph=graph, inputs=inputs, outputs=output_contract,
        units={"input": inputs, "output": output_contract},
        quality_rules={"contract": metadata.get("quality", metadata), "execution_kind": metadata["execution_kind"]},
        references=["docs/architecture/official-algorithm-migration.md"],
    )


def ensure_official_definitions(service) -> dict[str, str]:
    """Idempotently persist immutable definition records for registry manifests."""
    from app.models.algorithm_definition import DefinitionCreateRequest

    existing = {(item.name, item.owner): item for item in service.list()}
    persisted: dict[str, str] = {}
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        definition = existing.get((manifest.definition_name, "platform-official"))
        if definition is None:
            definition = service.create(DefinitionCreateRequest(name=manifest.definition_name, owner="platform-official", description=f"Frozen official {manifest.algorithm_id} contract; shadow migration only."))
        draft = official_definition_draft(manifest.algorithm_id)
        version = service.repository.get_version(definition.definition_id, draft.semver)
        if version is None:
            version = service.create_version(definition.definition_id, draft)
        if version.state != "published":
            version = service.publish(definition.definition_id, draft.semver)
        persisted[manifest.algorithm_id] = version.digest_sha256
    return persisted


def official_algorithm_catalog(service) -> list[OfficialAlgorithmCatalogItem]:
    """Resolve code manifests to installed immutable definition identities."""
    definitions = {(item.name, item.owner): item for item in service.list()}
    catalog: list[OfficialAlgorithmCatalogItem] = []
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        definition = definitions.get((manifest.definition_name, "platform-official"))
        if definition is None:
            raise RuntimeError(f"official definition missing: {manifest.algorithm_id}")
        version = service.repository.get_version(definition.definition_id, "1.0.0")
        if version is None or version.state != "published":
            raise RuntimeError(f"official definition version unavailable: {manifest.algorithm_id}")
        catalog.append(OfficialAlgorithmCatalogItem(
            algorithm_id=manifest.algorithm_id, display_name_zh=manifest.display_name_zh,
            abbreviation=manifest.abbreviation, purpose_zh=manifest.purpose_zh,
            scientific_version=manifest.scientific_version, implementation_identity=manifest.implementation_identity,
            execution_kind=manifest.execution_kind, availability=manifest.availability,
            is_runnable=manifest.is_runnable, required_channel_roles=manifest.required_channel_roles,
            supported_modes=manifest.supported_modes, definition_id=definition.definition_id,
            definition_version=version.semver,
        ))
    return catalog

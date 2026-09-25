"""Official algorithm catalog backed by Runtime manifests."""

from __future__ import annotations

from typing import Any

from app.algorithms.faa.manifest import MANIFEST as FAA_MANIFEST
from app.algorithms.band_ratio.manifest import MANIFEST as BAND_RATIO_MANIFEST
from app.algorithms.iapf.manifest import MANIFEST as IAPF_MANIFEST
from app.algorithms.peak_frequency.manifest import MANIFEST as PEAK_FREQUENCY_MANIFEST
from app.algorithms.rbp.manifest import MANIFEST as RBP_MANIFEST
from app.algorithms.theta_beta.manifest import MANIFEST as THETA_BETA_MANIFEST
from app.algorithm_runtime.contracts import AlgorithmManifest
from app.eeg_core.official_algorithms.brainbeat import BRAINBEAT_MANIFEST
from app.eeg_core.official_algorithms.contracts import OfficialAlgorithmCatalogItem
from app.models.algorithm_definition import DefinitionVersionDraft
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


OFFICIAL_ALGORITHM_MANIFESTS: tuple[AlgorithmManifest, ...] = (
    RBP_MANIFEST,
    BAND_RATIO_MANIFEST,
    PEAK_FREQUENCY_MANIFEST,
    THETA_BETA_MANIFEST,
    FAA_MANIFEST,
    BRAINBEAT_MANIFEST,
    IAPF_MANIFEST,
)


def _manifest(algorithm_id: str) -> AlgorithmManifest:
    for item in OFFICIAL_ALGORITHM_MANIFESTS:
        if item.algorithm_id == algorithm_id:
            return item
    raise ValueError(f"unknown official algorithm: {algorithm_id}")


def official_definition(algorithm_id: str) -> dict[str, object]:
    """Return stable compatibility metadata derived from the Runtime manifest."""
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
    elif algorithm_id == "peak_frequency":
        values.update({"inputs": ["PSD"], "formula": "argmax(PSD within declared frequency band)", "edge_policy": "inclusive", "tie_policy": "lowest_frequency_grid_point", "interpolation": "none"})
    elif algorithm_id == "band_ratio":
        values.update({"inputs": ["band_power numerator", "band_power denominator"], "formula": "numerator_power / denominator_power", "denominator_policy": "strictly_positive"})
    return values


def official_definition_draft(algorithm_id: str) -> DefinitionVersionDraft:
    """Return the immutable v1 definition used for catalog provenance."""
    manifest = _manifest(algorithm_id)
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
        output_contract = {
            field["name"]: {"type": "Scalar", "unit": field["unit"]}
            for field in manifest.output_schema["fields"]
        }
    return DefinitionVersionDraft(
        semver=str(metadata["version"]), graph=graph, inputs=inputs, outputs=output_contract,
        units={"input": inputs, "output": output_contract},
        quality_rules={"contract": metadata.get("quality", metadata), "execution_kind": metadata["execution_kind"]},
        references=["docs/architecture/official-algorithm-migration.md"],
    )


def ensure_official_definitions(service) -> dict[str, str]:
    """Idempotently persist immutable definition records for Runtime manifests."""
    from app.models.algorithm_definition import DefinitionCreateRequest

    existing = {(item.name, item.owner): item for item in service.list()}
    persisted: dict[str, str] = {}
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        if not manifest.definition_name:
            raise RuntimeError(f"official manifest is missing definition name: {manifest.algorithm_id}")
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


def official_definition_identity(service, algorithm_id: str) -> tuple[str, str, str]:
    manifest = _manifest(algorithm_id)
    if not manifest.definition_name:
        raise RuntimeError(f"official definition missing: {algorithm_id}")
    definition = next((item for item in service.list() if item.name == manifest.definition_name and item.owner == "platform-official"), None)
    if definition is None:
        raise RuntimeError(f"official definition missing: {algorithm_id}")
    version = service.repository.get_version(definition.definition_id, "1.0.0")
    if version is None or version.state != "published":
        raise RuntimeError(f"official definition version unavailable: {algorithm_id}")
    return definition.definition_id, version.semver, version.digest_sha256


def official_algorithm_catalog(service) -> list[OfficialAlgorithmCatalogItem]:
    definitions = {(item.name, item.owner): item for item in service.list()}
    catalog: list[OfficialAlgorithmCatalogItem] = []
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        if not manifest.definition_name:
            raise RuntimeError(f"official manifest is missing definition name: {manifest.algorithm_id}")
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
            supported_modes=manifest.supported_modes, output_schema=manifest.output_schema,
            definition_id=definition.definition_id, definition_version=version.semver,
        ))
    return catalog

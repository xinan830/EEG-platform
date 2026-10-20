"""Official algorithm catalog backed by Runtime manifests."""

from __future__ import annotations

from typing import Any

from app.algorithms.builtin_modules import BUILTIN_ALGORITHM_MANIFESTS
from app.algorithm_runtime.contracts import AlgorithmManifest
from app.core.provenance import sha256_json
from app.eeg_core.official_algorithms.contracts import OfficialAlgorithmCatalogItem
from app.models.algorithm_definition import DefinitionVersionDraft


OFFICIAL_ALGORITHM_MANIFESTS: tuple[AlgorithmManifest, ...] = BUILTIN_ALGORITHM_MANIFESTS


def _definition_version(algorithm_id: str) -> str:
    persistence = _manifest(algorithm_id).definition_persistence
    if persistence is None:
        raise ValueError(f"official algorithm {algorithm_id!r} lacks Definition persistence metadata")
    return persistence.version


def _manifest(algorithm_id: str) -> AlgorithmManifest:
    matches = [item for item in OFFICIAL_ALGORITHM_MANIFESTS
               if item.algorithm_id == algorithm_id and item.is_current_version]
    if len(matches) != 1:
        raise ValueError(f"official algorithm {algorithm_id!r} must have exactly one current manifest")
    return matches[0]


def official_definition(algorithm_id: str) -> dict[str, object]:
    """Return stable compatibility metadata derived from the Runtime manifest."""
    manifest = _manifest(algorithm_id)
    persistence = manifest.definition_persistence
    if persistence is None:
        raise ValueError(f"official algorithm {algorithm_id!r} lacks Definition persistence metadata")
    return {
        "version": persistence.version,
        "implementation": manifest.implementation_identity,
        "execution_kind": manifest.execution_kind,
        "availability": manifest.availability,
        **persistence.definition_metadata,
    }


def official_definition_draft(algorithm_id: str) -> DefinitionVersionDraft:
    """Return the current immutable definition used for catalog provenance."""
    manifest = _manifest(algorithm_id)
    persistence = manifest.definition_persistence
    if persistence is None:
        raise ValueError(f"official algorithm {algorithm_id!r} lacks Definition persistence metadata")
    metadata = official_definition(algorithm_id)
    # The immutable Definition engine remains a storage/provenance projection,
    # not the scientific executor. Only modules that explicitly declare a
    # valid graph (currently RBP) supply one; other modules use the generic
    # official-result adapter with output fields taken from their manifest.
    graph = persistence.graph or {
        "nodes": [{
            "id": "out", "type": "output",
            "inputs": {"source": "$input.official_result"}, "parameters": {},
        }],
        "outputs": ["out"],
    }
    inputs = persistence.inputs or {
        "official_result": {
            "type": "Scalar", "unit": "dimensionless_or_declared_output",
            "source": "official_composite_adapter",
        },
    }
    outputs = persistence.outputs or {
        field["name"]: {"type": "Scalar", "unit": field["unit"]}
        for field in manifest.output_schema["fields"]
    }
    return DefinitionVersionDraft(
        semver=persistence.version,
        graph=graph,
        inputs=inputs,
        outputs=outputs,
        units={"input": inputs, "output": outputs},
        parameter_schema=persistence.definition_metadata.get("parameter_schema", {}),
        quality_rules={"contract": metadata, "execution_kind": manifest.execution_kind},
        # Keep the published immutable Definition reference list stable. Richer
        # module validation references live in capability evidence metadata.
        references=["docs/architecture/official-algorithm-migration.md"],
    )


def ensure_official_definitions(service) -> dict[str, str]:
    """Idempotently persist immutable definition records for Runtime manifests."""
    from app.models.algorithm_definition import DefinitionCreateRequest

    existing = {(item.name, item.owner): item for item in service.list()}
    persisted: dict[str, str] = {}
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        if not manifest.is_current_version:
            continue
        if manifest.definition_persistence is None:
            raise RuntimeError(f"official manifest is missing definition name: {manifest.algorithm_id}")
        definition = existing.get((manifest.definition_persistence.name, "platform-official"))
        if definition is None:
            definition = service.create(DefinitionCreateRequest(name=manifest.definition_persistence.name, owner="platform-official", description=f"Frozen official {manifest.algorithm_id} contract; shadow migration only."))
        draft = official_definition_draft(manifest.algorithm_id)
        version = service.repository.get_version(definition.definition_id, draft.semver)
        # RBP's published Definition graph is a frozen executable contract and
        # has always enforced digest parity. Other official Definitions are
        # provenance records for the official-result adapter: deployed databases
        # may contain an older immutable descriptive payload at the same
        # Definition semver. Preserve and identify that stored record; do not
        # rewrite it or treat it as a calculator compatibility path.
        if (version is not None and manifest.algorithm_id == "rbp"
                and version.digest_sha256 != sha256_json(draft.model_dump(mode="json"))):
            raise RuntimeError("official RBP Definition version differs from the installed scientific contract")
        if version is None:
            version = service.create_version(definition.definition_id, draft)
        if version.state != "published":
            version = service.publish(definition.definition_id, draft.semver)
        persisted[manifest.algorithm_id] = version.digest_sha256
    return persisted


def official_definition_identity(service, algorithm_id: str) -> tuple[str, str, str]:
    manifest = _manifest(algorithm_id)
    if manifest.definition_persistence is None:
        raise RuntimeError(f"official definition missing: {algorithm_id}")
    definition = next((item for item in service.list() if item.name == manifest.definition_persistence.name and item.owner == "platform-official"), None)
    if definition is None:
        raise RuntimeError(f"official definition missing: {algorithm_id}")
    version = service.repository.get_version(definition.definition_id, _definition_version(algorithm_id))
    if version is None or version.state != "published":
        raise RuntimeError(f"official definition version unavailable: {algorithm_id}")
    return definition.definition_id, version.semver, version.digest_sha256


def official_algorithm_catalog(service) -> list[OfficialAlgorithmCatalogItem]:
    definitions = {(item.name, item.owner): item for item in service.list()}
    catalog: list[OfficialAlgorithmCatalogItem] = []
    for manifest in OFFICIAL_ALGORITHM_MANIFESTS:
        if not manifest.is_current_version:
            continue
        if manifest.definition_persistence is None:
            raise RuntimeError(f"official manifest is missing definition name: {manifest.algorithm_id}")
        definition = definitions.get((manifest.definition_persistence.name, "platform-official"))
        if definition is None:
            raise RuntimeError(f"official definition missing: {manifest.algorithm_id}")
        version = service.repository.get_version(definition.definition_id, _definition_version(manifest.algorithm_id))
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

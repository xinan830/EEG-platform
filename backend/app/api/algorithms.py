"""Unified readable catalog for official and user algorithms."""

from __future__ import annotations

from fastapi import APIRouter, Request

from app.bootstrap import build_builtin_registry
from app.algorithm_runtime.errors import AlgorithmRuntimeError
from app.algorithms.catalog import ensure_official_definitions, official_algorithm_catalog


router = APIRouter(prefix="/api/algorithms", tags=["algorithms"])


def _official_items(request: Request) -> list[dict[str, object]]:
    ensure_official_definitions(request.app.state.definition_service)
    registry = getattr(request.app.state, "algorithm_runtime_registry", None)
    if registry is None:
        registry = build_builtin_registry()
    official_items = official_algorithm_catalog(request.app.state.definition_service)
    items: list[dict[str, object]] = []
    for official in official_items:
        module = registry.get_current(official.algorithm_id)
        manifest = module.manifest
        if (
            manifest.algorithm_id != official.algorithm_id
            or manifest.scientific_version != official.scientific_version
            or not manifest.is_current_version
            or manifest.execution_kind != official.execution_kind
            or manifest.implementation_identity != official.implementation_identity
            or manifest.display_name_zh != official.display_name_zh
            or manifest.abbreviation != official.abbreviation
            or manifest.purpose_zh != official.purpose_zh
            or manifest.supported_modes != official.supported_modes
            or manifest.output_schema != official.output_schema
            or manifest.output_contract is None
            or manifest.definition_persistence is None
        ):
            raise AlgorithmRuntimeError("official catalog/runtime manifest mismatch")
        items.append({
            "source": "official",
            "id": manifest.algorithm_id,
            "version": manifest.scientific_version,
            "display_name_zh": manifest.display_name_zh,
            "abbreviation": manifest.abbreviation,
            "description": manifest.purpose_zh,
            "parameters": [item.model_dump(mode="json") for item in module.parameter_schema().parameters],
            "modes": list(manifest.supported_modes),
            "output_schema": manifest.output_schema,
            "output_contract": manifest.output_contract.model_dump(mode="json"),
            "dynamic_policy": manifest.dynamic_policy.model_dump(mode="json"),
            "dynamic_capabilities": (
                manifest.dynamic_capabilities.model_dump(mode="json")
                if manifest.dynamic_capabilities is not None else None
            ),
            "availability": official.availability,
            "is_runnable": official.is_runnable,
            "definition_id": official.definition_id,
            "definition_version": official.definition_version,
            "implementation_identity": manifest.implementation_identity,
        })
    return items


@router.get("")
def list_algorithms(request: Request) -> dict[str, object]:
    try:
        return {"algorithms": _official_items(request)}
    except (AlgorithmRuntimeError, RuntimeError):
        return {"algorithms": [], "official_catalog_error": "OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE"}

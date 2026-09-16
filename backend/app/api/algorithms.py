"""Unified readable catalog for official and user algorithms."""

from __future__ import annotations

from fastapi import APIRouter, Request

from app.algorithm_runtime.builtins import build_builtin_registry
from app.algorithm_runtime.errors import AlgorithmRuntimeError


router = APIRouter(prefix="/api/algorithms", tags=["algorithms"])


def _official_items(request: Request) -> list[dict[str, object]]:
    registry = getattr(request.app.state, "algorithm_runtime_registry", None)
    if registry is None:
        registry = build_builtin_registry()
    items: list[dict[str, object]] = []
    for module in registry.list():
        manifest = module.manifest
        items.append({
            "source": "official",
            "id": manifest.algorithm_id,
            "version": manifest.scientific_version,
            "display_name_zh": manifest.display_name_zh,
            "abbreviation": manifest.abbreviation,
            "description": manifest.purpose_zh,
            "parameters": [item.model_dump(mode="json") for item in module.parameter_schema().parameters],
            "modes": list(manifest.supported_modes),
            "output": {"unit": manifest.output_unit},
            "availability": "available",
            "is_runnable": True,
        })
    return items


def _user_items(request: Request) -> list[dict[str, object]]:
    service = request.app.state.definition_service
    items: list[dict[str, object]] = []
    for definition in service.list():
        if definition.owner != "local-user":
            continue
        versions = [item for item in service.list_versions(definition.definition_id) if item.state == "published"]
        if not versions:
            continue
        version = sorted(versions, key=lambda item: item.semver)[-1]
        items.append({
            "source": "user",
            "id": definition.definition_id,
            "version": version.semver,
            "display_name_zh": definition.name,
            "abbreviation": definition.name,
            "description": definition.description,
            "parameters": version.parameter_schema,
            "modes": ["static", "dynamic"],
            "output": version.outputs,
            "availability": "available",
            "is_runnable": True,
        })
    return items


@router.get("")
def list_algorithms(request: Request) -> dict[str, object]:
    try:
        return {"algorithms": _official_items(request) + _user_items(request)}
    except AlgorithmRuntimeError:
        return {"algorithms": _user_items(request), "official_catalog_error": "OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE"}

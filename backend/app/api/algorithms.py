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
    runtime_modules = {}
    for official in official_items:
        try:
            runtime_modules[official.algorithm_id] = registry.get_current(official.algorithm_id)
        except AlgorithmRuntimeError:
            pass
    items: list[dict[str, object]] = []
    for official in official_items:
        module = runtime_modules.get(official.algorithm_id)
        if module is None:
            items.append({
                "source": "official", "id": official.algorithm_id,
                "version": official.scientific_version,
                "display_name_zh": official.display_name_zh,
                "abbreviation": official.abbreviation,
                "description": official.purpose_zh,
                "parameters": [], "modes": list(official.supported_modes),
                "output_schema": official.output_schema,
                "dynamic_policy": {"minimum_window_s": 4.0, "window_options_s": [5.0, 10.0, 20.0, 30.0], "default_window_s": 5.0, "refresh_step_s": 1.0, "refresh_step_options_s": [1.0, 2.0, 5.0], "allow_warmup": True},
                "availability": official.availability, "is_runnable": False,
                "definition_id": official.definition_id,
                "definition_version": official.definition_version,
                "implementation_identity": official.implementation_identity,
                "dynamic_capabilities": None,
            })
            continue
        manifest = module.manifest
        if (
            manifest.scientific_version != official.scientific_version
            or not manifest.is_current_version
            or manifest.execution_kind != official.execution_kind
            or manifest.implementation_identity != official.implementation_identity
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

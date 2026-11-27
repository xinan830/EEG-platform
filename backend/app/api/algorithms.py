"""Unified readable catalog for official and user algorithms."""

from __future__ import annotations

from fastapi import APIRouter, Request

from app.algorithm_runtime.errors import AlgorithmRuntimeError
from app.services.official_algorithm_catalog import available_official_algorithms


router = APIRouter(prefix="/api/algorithms", tags=["algorithms"])


@router.get("")
def list_algorithms(request: Request) -> dict[str, object]:
    try:
        return {"algorithms": available_official_algorithms(
            request.app.state.definition_service, request.app.state.algorithm_runtime_registry,
        )}
    except (AlgorithmRuntimeError, RuntimeError):
        return {"algorithms": [], "official_catalog_error": "OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE"}

"""User-triggered cache reset for the connected local backend."""

import logging
from time import perf_counter

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from app.core.api_contract import error_response
from app.models.cache_maintenance import CacheClearRequest, CacheClearResponse
from app.services.cache_maintenance import CacheMaintenanceError


router = APIRouter(prefix="/api/maintenance/cache", tags=["maintenance"])
logger = logging.getLogger("brain_platform.cache_maintenance")


@router.post("/clear", response_model=CacheClearResponse)
def clear_cache(_payload: CacheClearRequest, request: Request) -> CacheClearResponse | JSONResponse:
    started = perf_counter()
    request_id = getattr(request.state, "request_id", "unknown")
    try:
        value = request.app.state.cache_maintenance_service.clear()
    except CacheMaintenanceError as exc:
        request.app.state.audit_service.record("cache.clear", request_id, outcome=exc.code)
        logger.warning("cache_clear_failed request_id=%s code=%s elapsed_ms=%.1f",
                       request_id, exc.code, (perf_counter() - started) * 1000)
        return error_response(request, exc.status_code, exc.code, str(exc))
    request.app.state.audit_service.record("cache.clear", request_id, parameters=value.model_dump())
    logger.info("cache_cleared request_id=%s deleted_windows=%d deleted_sessions=%d deleted_attempts=%d "
                "deleted_subscriptions=%d deleted_files=%d freed_bytes=%d elapsed_ms=%.1f",
                request_id, value.deleted_windows, value.deleted_sessions, value.deleted_attempts,
                value.deleted_subscriptions, value.deleted_files, value.freed_bytes,
                (perf_counter() - started) * 1000)
    return value

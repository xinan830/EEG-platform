"""Scalar-only reads from already computed analysis windows."""

import logging
from time import perf_counter

from fastapi import APIRouter, Query, Request
from starlette.responses import Response

from app.core.api_contract import error_response
from app.services.result_scalar_inputs import ResultProjectionUnavailable

logger = logging.getLogger("brain_platform.result_scalars")
router = APIRouter(prefix="/api/analysis-windows", tags=["result-scalars"])


@router.get("/{window_id}/scalar-values")
def window_scalar_values(window_id: str, request: Request,
                         field_ids: list[str] = Query(..., min_length=1, max_length=5),
                         project_id: str | None = Query(default=None)):
    try:
        started = perf_counter()
        result = request.app.state.result_scalars_service.read_window(
            window_id, project_id=project_id, field_ids=field_ids)
        payload = result.model_dump_json()
        logger.info("result_scalars_perf request_id=%s window_id=%s fields=%d bytes=%d total_ms=%.1f",
            getattr(request.state, "request_id", None) or request.headers.get("X-Request-ID", "unknown"),
            window_id, len(result.fields), len(payload.encode("utf-8")), (perf_counter() - started) * 1000)
        return Response(content=payload, media_type="application/json")
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except ResultProjectionUnavailable as exc:
        status = 422 if exc.code in {"TOPOMAP_FIELD_IDS_REQUIRED", "TOPOMAP_FIELD_ID_INVALID"} else 409
        return error_response(request, status, exc.code, exc.message)
    except ValueError as exc:
        return error_response(request, 409, "SCALAR_PROJECTION_UNAVAILABLE", str(exc))

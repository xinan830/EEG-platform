import logging
from time import perf_counter
from fastapi import APIRouter, Query, Request
from typing import Literal
from starlette.responses import Response

from app.core.api_contract import error_response
from app.services.artifacts import ArtifactIntegrityError, WindowIndexUnavailable
from app.services.topomap import TopomapUnavailable

logger = logging.getLogger("brain_platform.topomap")
BandId = Literal["delta", "theta", "alpha", "beta", "gamma"]


router = APIRouter(prefix="/api/runs", tags=["topomap"])


def _request_id(request: Request) -> str:
    return str(getattr(request.state, "request_id", None)
               or request.headers.get("X-Request-ID") or "unknown")


def _json_response(request: Request, response, *, result_id: str, field_ids: list[str],
                   started: float) -> Response:
    service_ms = (perf_counter() - started) * 1000
    serialization_started = perf_counter()
    payload = response.model_dump_json()
    serialization_ms = (perf_counter() - serialization_started) * 1000
    grid_cells = sum((field.grid.width * field.grid.height) if field.grid else 0 for field in response.fields)
    logger.info(
        "topomap_api_perf request_id=%s result_id=%s recording_id=%s field_ids=%s "
        "field_count=%d grid_cells=%d service_ms=%.1f serialization_ms=%.1f",
        _request_id(request),
        result_id, response.recording_id, ",".join(field_ids), len(response.fields),
        grid_cells, service_ms, serialization_ms,
    )
    return Response(content=payload, media_type="application/json")


@router.get("/{run_id}/topomap")
def rbp_topomap(
    run_id: str,
    request: Request,
    cursor_sample: int | None = Query(default=None, ge=0),
    cursor_s: float | None = Query(default=None, ge=0),
    field_ids: list[str] = Query(..., min_length=1),
):
    if (cursor_sample is None) == (cursor_s is None):
        return error_response(request, 422, "TOPOMAP_CURSOR_INVALID", "地形图必须且只能提供一种游标坐标。")
    try:
        started = perf_counter()
        response = request.app.state.topomap_service.build_rbp_window(
            run_id, cursor_sample=cursor_sample, cursor_s=cursor_s, field_ids=field_ids,
            request_id=_request_id(request),
        )
        return _json_response(request, response, result_id=run_id, field_ids=field_ids, started=started)
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except TopomapUnavailable as exc:
        status = 422 if exc.code in {"TOPOMAP_CURSOR_INVALID", "TOPOMAP_FIELD_IDS_REQUIRED", "TOPOMAP_FIELD_ID_INVALID"} else 409
        message = exc.message if not exc.detail else f"{exc.message}（{exc.detail}）"
        return error_response(request, status, exc.code, message)
    except ArtifactIntegrityError:
        return error_response(request, 409, "ARTIFACT_INTEGRITY_FAILED", "结果文件校验失败")
    except WindowIndexUnavailable as exc:
        return error_response(request, 409, "RESULT_WINDOW_INDEX_UNAVAILABLE", str(exc))
    except ValueError as exc:
        return error_response(request, 409, "TOPOMAP_UNAVAILABLE", str(exc))


dynamic_router = APIRouter(prefix="/api/analysis-windows", tags=["topomap"])


@dynamic_router.get("/{window_id}/topomap")
def dynamic_topomap(
    window_id: str,
    request: Request,
    project_id: str | None = Query(default=None),
    field_ids: list[str] = Query(..., min_length=1),
):
    try:
        started = perf_counter()
        response = request.app.state.topomap_service.build_dynamic_window(
            window_id, project_id=project_id, field_ids=field_ids,
            request_id=_request_id(request),
        )
        return _json_response(request, response, result_id=window_id, field_ids=field_ids, started=started)
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except TopomapUnavailable as exc:
        status = 422 if exc.code in {"TOPOMAP_FIELD_IDS_REQUIRED", "TOPOMAP_FIELD_ID_INVALID"} else 409
        return error_response(request, status, exc.code, exc.message)
    except ValueError as exc:
        return error_response(request, 409, "TOPOMAP_UNAVAILABLE", str(exc))

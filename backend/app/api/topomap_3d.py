"""Read-only 3D GNM model and RBP surface-field endpoints."""

import logging
from time import perf_counter
from typing import Literal

from fastapi import APIRouter, Query, Request
from starlette.responses import Response

from app.core.api_contract import error_response
from app.models.topomap_3d import (
    Topomap3DLocationRequest,
    Topomap3DLocationResponse,
    Topomap3DModelResponse,
    Topomap3DResponse,
)
from app.services.artifacts import ArtifactIntegrityError, WindowIndexUnavailable
from app.services.topomap import TopomapUnavailable
from app.services.topomap_3d_assets import MappingAssetsInvalid


BandId = Literal["delta", "theta", "alpha", "beta", "gamma"]
model_router = APIRouter(prefix="/api/topomap/3d", tags=["topomap-3d"])
run_router = APIRouter(prefix="/api/runs", tags=["topomap-3d"])
dynamic_router = APIRouter(prefix="/api/analysis-windows", tags=["topomap-3d"])
logger = logging.getLogger("brain_platform.topomap_3d")


def _request_id(request: Request) -> str:
    return str(getattr(request.state, "request_id", None)
               or request.headers.get("X-Request-ID") or "unknown")


def _json_response(request: Request, response: Topomap3DModelResponse | Topomap3DResponse,
                   *, result_id: str, started: float) -> Response:
    serialize_started = perf_counter()
    content = response.model_dump_json()
    serialize_ms = (perf_counter() - serialize_started) * 1000
    logger.info("topomap_3d_api_perf request_id=%s result_id=%s status=%s serialize_ms=%.1f total_ms=%.1f",
        _request_id(request), result_id, getattr(response, "status", "model"),
        serialize_ms,
        (perf_counter() - started) * 1000)
    return Response(content=content, media_type="application/json")


def _unavailable(request: Request, error: TopomapUnavailable):
    return error_response(request, 409, error.code, error.message)


@model_router.get("/model", response_model=Topomap3DModelResponse)
def get_topomap_3d_model(request: Request):
    started = perf_counter()
    try:
        result = request.app.state.topomap_3d_service.get_model()
        return _json_response(request, result, result_id=result.model_id, started=started)
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@model_router.post("/locations", response_model=Topomap3DLocationResponse)
def resolve_topomap_3d_locations(body: Topomap3DLocationRequest, request: Request):
    try:
        response = request.app.state.topomap_3d_service.resolve_locations(body.channels)
        return Response(content=response.model_dump_json(), media_type="application/json")
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@run_router.get("/{run_id}/topomap/3d", response_model=Topomap3DResponse)
def rbp_topomap_3d(
    run_id: str,
    request: Request,
    cursor_sample: int | None = Query(default=None, ge=0),
    cursor_s: float | None = Query(default=None, ge=0),
    power_type: Literal["relative", "absolute"] = Query(default="relative"),
    band_id: BandId | None = Query(default=None),
):
    if (cursor_sample is None) == (cursor_s is None):
        return error_response(request, 422, "TOPOMAP_CURSOR_INVALID", "地形图必须且只能提供一种游标坐标。")
    started = perf_counter()
    try:
        result = request.app.state.topomap_3d_service.build_run_window(
            run_id, cursor_sample=cursor_sample, cursor_s=cursor_s, power_type=power_type,
            band_id=band_id, request_id=_request_id(request))
        return _json_response(request, result, result_id=run_id, started=started)
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except TopomapUnavailable as error:
        return _unavailable(request, error)
    except (ArtifactIntegrityError, WindowIndexUnavailable) as error:
        code = "ARTIFACT_INTEGRITY_FAILED" if isinstance(error, ArtifactIntegrityError) else "RESULT_WINDOW_INDEX_UNAVAILABLE"
        return error_response(request, 409, code, str(error))
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@dynamic_router.get("/{window_id}/topomap/3d", response_model=Topomap3DResponse)
def dynamic_topomap_3d(
    window_id: str,
    request: Request,
    project_id: str | None = Query(default=None),
    power_type: Literal["relative", "absolute"] = Query(default="relative"),
    band_id: BandId | None = Query(default=None),
):
    started = perf_counter()
    try:
        result = request.app.state.topomap_3d_service.build_dynamic_window(
            window_id, project_id=project_id, power_type=power_type, band_id=band_id,
            request_id=_request_id(request))
        return _json_response(request, result, result_id=window_id, started=started)
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except TopomapUnavailable as error:
        return _unavailable(request, error)
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))

"""Read-only GNM model and standard electrode-location endpoints."""

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse
from starlette.responses import Response

from app.core.api_contract import error_response
from app.models.topomap_3d import (
    Topomap3DLocationRequest,
    Topomap3DLocationResponse,
    Topomap3DModelResponse,
    Topomap3DMontageCatalogResponse,
    Topomap3DMontagePreviewResponse,
)
from app.services.analysis.results.topomap_3d_assets import MappingAssetsInvalid
from app.services.analysis.results.topomap_3d_cortex import load_cortex_atlas_payload


router = APIRouter(prefix="/api/topomap/3d", tags=["electrode-locations-3d"])


def _json_response(
    response: (
        Topomap3DModelResponse
        | Topomap3DLocationResponse
        | Topomap3DMontageCatalogResponse
        | Topomap3DMontagePreviewResponse
    ),
) -> Response:
    return Response(content=response.model_dump_json(), media_type="application/json")


@router.get("/model", response_model=Topomap3DModelResponse)
def get_topomap_3d_model(request: Request):
    try:
        return _json_response(request.app.state.topomap_3d_service.get_model())
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@router.post("/locations", response_model=Topomap3DLocationResponse)
def resolve_topomap_3d_locations(body: Topomap3DLocationRequest, request: Request):
    try:
        return _json_response(request.app.state.topomap_3d_service.resolve_locations(body.channels))
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@router.get("/cortex")
def get_topomap_3d_cortex(request: Request):
    try:
        return JSONResponse(content=load_cortex_atlas_payload())
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_CORTEX_INVALID", str(error))


@router.get("/montages", response_model=Topomap3DMontageCatalogResponse)
def list_topomap_3d_montages(request: Request):
    try:
        return _json_response(request.app.state.topomap_3d_service.list_montages())
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))


@router.get("/montages/{montage_name}", response_model=Topomap3DMontagePreviewResponse)
def get_topomap_3d_montage_preview(montage_name: str, request: Request):
    try:
        return _json_response(request.app.state.topomap_3d_service.preview_montage(montage_name))
    except KeyError:
        return error_response(request, 404, "MONTAGE_NOT_FOUND", f"未找到内置电极模板: {montage_name}")
    except MappingAssetsInvalid as error:
        return error_response(request, 409, "TOPOMAP_3D_ASSET_INVALID", str(error))

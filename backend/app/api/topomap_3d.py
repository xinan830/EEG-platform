"""Read-only GNM model and standard electrode-location endpoints."""

from fastapi import APIRouter, Request
from starlette.responses import Response

from app.core.api_contract import error_response
from app.models.topomap_3d import Topomap3DLocationRequest, Topomap3DLocationResponse, Topomap3DModelResponse
from app.services.topomap_3d_assets import MappingAssetsInvalid


router = APIRouter(prefix="/api/topomap/3d", tags=["electrode-locations-3d"])


def _json_response(response: Topomap3DModelResponse | Topomap3DLocationResponse) -> Response:
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

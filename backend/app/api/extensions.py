"""Local extension governance resources; deliberately not a plugin loader."""

from fastapi import APIRouter, Request

from app.core.api_contract import error_response
from app.models.extension_manifest import ExtensionManifest
from app.services.extension_governance import ExtensionGovernanceError


router = APIRouter(prefix="/api/extensions", tags=["extension-governance"])


@router.get("/capabilities")
def capabilities(request: Request) -> dict[str, object]:
    return request.app.state.extension_governance_service.capabilities()


@router.post("/validate")
def validate(payload: ExtensionManifest, request: Request) -> dict[str, object]:
    try:
        return request.app.state.extension_governance_service.validate(payload)
    except ExtensionGovernanceError as exc:
        return error_response(request, 422, exc.code, str(exc))

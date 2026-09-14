"""Engineering validation run resources."""

from fastapi import APIRouter, Query, Request, status

from app.core.api_contract import error_response
from app.models.run import ValidationCreateRequest
from app.services.validations import ValidationService


router = APIRouter(prefix="/api/validations", tags=["validations"])


def _service(request: Request) -> ValidationService:
    return request.app.state.validation_service


@router.post("", status_code=status.HTTP_201_CREATED)
def create_validation(payload: ValidationCreateRequest, request: Request):
    try:
        result = _service(request).create(payload)
    except ValueError as exc:
        return error_response(request, 422, "VALIDATION_REQUEST_INVALID", str(exc))
    return result.model_dump(mode="json")


@router.get("")
def list_validations(request: Request, limit: int = Query(100, ge=1, le=1000)):
    return [item.model_dump(mode="json") for item in _service(request).list(limit)]


@router.get("/{validation_id}")
def get_validation(validation_id: str, request: Request):
    try:
        return _service(request).get(validation_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "VALIDATION_NOT_FOUND", "算法校验不存在")


@router.get("/{validation_id}/report")
def get_validation_report(validation_id: str, request: Request):
    try:
        return _service(request).report(validation_id)
    except KeyError:
        return error_response(request, 404, "VALIDATION_NOT_FOUND", "算法校验不存在")

"""Immutable algorithm definition resources."""

from fastapi import APIRouter, Request, status
from pydantic import BaseModel, Field

from app.core.api_contract import error_response
from app.eeg_core.definition_engine import DefinitionEngineError
from app.models.algorithm_definition import DefinitionCreateRequest, DefinitionVersionDraft
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.primitives.units import Unit
from app.services.definitions import DefinitionService

router = APIRouter(prefix="/api/algorithm-definitions", tags=["algorithm-definitions"])


class ValidateRequest(BaseModel):
    draft: DefinitionVersionDraft
    parameters: dict[str, object] = Field(default_factory=dict)


class CloneRequest(BaseModel):
    name: str | None = Field(default=None, min_length=1, max_length=160)


class ScalarInput(BaseModel):
    value: float
    unit: Unit


class PreviewRequest(BaseModel):
    draft: DefinitionVersionDraft
    inputs: dict[str, ScalarInput]


def _service(request: Request) -> DefinitionService:
    return request.app.state.definition_service


@router.post("", status_code=status.HTTP_201_CREATED)
def create(payload: DefinitionCreateRequest, request: Request):
    return _service(request).create(payload).model_dump(mode="json")


@router.get("")
def list_definitions(request: Request):
    return [item.model_dump(mode="json") for item in _service(request).list()]


@router.post("/validate")
def validate(payload: ValidateRequest, request: Request):
    try:
        return _service(request).validate(payload.draft, payload.parameters)
    except DefinitionEngineError as exc:
        return error_response(request, 422, exc.code, str(exc))


@router.post("/preview")
def preview(payload: PreviewRequest, request: Request):
    try:
        values = {name: Scalar(input_value.value, input_value.unit) for name, input_value in payload.inputs.items()}
        result = _service(request).preview(payload.draft, values)
        return {"preview": True, "persisted": False, "outputs": {key: value.__dict__ for key, value in result["outputs"].items()}}
    except DefinitionEngineError as exc:
        return error_response(request, 422, exc.code, str(exc))


@router.post("/{definition_id}/versions", status_code=status.HTTP_201_CREATED)
def create_version(definition_id: str, payload: DefinitionVersionDraft, request: Request):
    try:
        return _service(request).create_version(definition_id, payload).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "DEFINITION_NOT_FOUND", "算法定义不存在")
    except (DefinitionEngineError, ValueError) as exc:
        return error_response(request, 422, getattr(exc, "code", "DEFINITION_INVALID"), str(exc))


@router.post("/{definition_id}/versions/{semver}/publish")
def publish(definition_id: str, semver: str, request: Request):
    try:
        return _service(request).publish(definition_id, semver).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "DEFINITION_VERSION_NOT_FOUND", "算法定义版本不存在")
    except DefinitionEngineError as exc:
        return error_response(request, 422, exc.code, str(exc))


@router.get("/{definition_id}")
def get(definition_id: str, request: Request):
    try:
        return _service(request).get(definition_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "DEFINITION_NOT_FOUND", "算法定义不存在")


@router.post("/{definition_id}/clone", status_code=status.HTTP_201_CREATED)
def clone(definition_id: str, payload: CloneRequest, request: Request):
    try:
        return _service(request).clone(definition_id, payload.name).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "DEFINITION_NOT_FOUND", "算法定义不存在")


@router.get("/{definition_id}/versions")
def list_versions(definition_id: str, request: Request):
    try:
        return [item.model_dump(mode="json") for item in _service(request).list_versions(definition_id)]
    except KeyError:
        return error_response(request, 404, "DEFINITION_NOT_FOUND", "算法定义不存在")


@router.get("/{definition_id}/versions/{left}/compare/{right}")
def compare(definition_id: str, left: str, right: str, request: Request):
    try:
        result = _service(request).compare(definition_id, left, right)
        return {key: value.model_dump(mode="json") if hasattr(value, "model_dump") else value for key, value in result.items()}
    except KeyError:
        return error_response(request, 404, "DEFINITION_VERSION_NOT_FOUND", "算法定义版本不存在")

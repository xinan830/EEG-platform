"""Read historical Definitions without exposing retired user authoring."""

from fastapi import APIRouter, Request

from app.core.api_contract import error_response
from app.services.definitions import DefinitionService


router = APIRouter(prefix="/api/algorithm-definitions", tags=["algorithm-definitions"])


def _service(request: Request) -> DefinitionService:
    return request.app.state.definition_service


def _retired(request: Request):
    return error_response(
        request, 410, "USER_ALGORITHM_AUTHORING_RETIRED",
        "用户自定义算法已停止创建和预览；历史定义与结果仍可读取",
    )


@router.get("/capabilities")
def capabilities(request: Request):
    return _retired(request)


@router.post("")
def create(request: Request):
    return _retired(request)


@router.get("")
def list_definitions(request: Request):
    return [item.model_dump(mode="json") for item in _service(request).list()]


@router.post("/validate")
def validate(request: Request):
    return _retired(request)


@router.post("/preview")
def preview(request: Request):
    return _retired(request)


@router.post("/preview-run")
def preview_run(request: Request):
    return _retired(request)


@router.post("/{definition_id}/versions")
def create_version(definition_id: str, request: Request):
    return _retired(request)


@router.post("/{definition_id}/versions/{semver}/publish")
def publish(definition_id: str, semver: str, request: Request):
    return _retired(request)


@router.get("/{definition_id}")
def get(definition_id: str, request: Request):
    try:
        return _service(request).get(definition_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "DEFINITION_NOT_FOUND", "算法定义不存在")


@router.delete("/{definition_id}")
def delete(definition_id: str, request: Request):
    return _retired(request)


@router.post("/{definition_id}/clone")
def clone(definition_id: str, request: Request):
    return _retired(request)


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

"""Local research hierarchy HTTP resources."""

from collections.abc import Callable

from fastapi import APIRouter, Request, status

from app.core.api_contract import error_response
from app.models.research_project import ConditionCreateRequest, ProjectCreateRequest, SessionCreateRequest, SubjectCreateRequest
from app.services.projects import ProjectConflictError, ProjectService


router = APIRouter(prefix="/api/projects", tags=["projects"])


def _service(request: Request) -> ProjectService:
    return request.app.state.project_service


@router.post("", status_code=status.HTTP_201_CREATED)
def create_project(payload: ProjectCreateRequest, request: Request):
    return _service(request).create(payload).model_dump(mode="json")


@router.get("")
def list_projects(request: Request):
    return [item.model_dump(mode="json") for item in _service(request).list()]


@router.get("/{project_id}")
def get_project(project_id: str, request: Request):
    item = _service(request).get(project_id)
    return item.model_dump(mode="json") if item else error_response(request, 404, "PROJECT_NOT_FOUND", "项目不存在")


@router.post("/{project_id}/subjects", status_code=status.HTTP_201_CREATED)
def create_subject(project_id: str, payload: SubjectCreateRequest, request: Request):
    return _mutate(request, lambda: _service(request).create_subject(project_id, payload))


@router.get("/{project_id}/subjects")
def list_subjects(project_id: str, request: Request):
    return _read(request, lambda: _service(request).list_subjects(project_id))


@router.post("/{project_id}/conditions", status_code=status.HTTP_201_CREATED)
def create_condition(project_id: str, payload: ConditionCreateRequest, request: Request):
    return _mutate(request, lambda: _service(request).create_condition(project_id, payload))


@router.get("/{project_id}/conditions")
def list_conditions(project_id: str, request: Request):
    return _read(request, lambda: _service(request).list_conditions(project_id))


@router.post("/{project_id}/sessions", status_code=status.HTTP_201_CREATED)
def create_session(project_id: str, payload: SessionCreateRequest, request: Request):
    return _mutate(request, lambda: _service(request).create_session(project_id, payload))


@router.get("/{project_id}/sessions")
def list_sessions(project_id: str, request: Request):
    return _read(request, lambda: _service(request).list_sessions(project_id))


@router.get("/{project_id}/recordings")
def list_project_recordings(project_id: str, request: Request):
    try:
        return {"recording_ids": sorted(_service(request).project_recording_ids(project_id))}
    except KeyError:
        return error_response(request, 404, "PROJECT_NOT_FOUND", "项目不存在")


def _read(request: Request, operation: Callable[[], list]):
    try:
        return [item.model_dump(mode="json") for item in operation()]
    except KeyError:
        return error_response(request, 404, "PROJECT_NOT_FOUND", "项目不存在")


def _mutate(request: Request, operation: Callable[[], object]):
    try:
        return operation().model_dump(mode="json")
    except ProjectConflictError as exc:
        code = "SUBJECT_CODE_CONFLICT" if "subject" in str(exc) else "CONDITION_CODE_CONFLICT"
        return error_response(request, 409, code, str(exc))
    except KeyError as exc:
        code = "RECORDING_NOT_FOUND" if "recording" in str(exc) else "PROJECT_REFERENCE_NOT_FOUND"
        return error_response(request, 404, code, "项目关联资源不存在")

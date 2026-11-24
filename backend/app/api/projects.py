"""Local research hierarchy HTTP resources."""

from collections.abc import Callable

from fastapi import APIRouter, File, Form, Request, UploadFile, status

from app.core.api_contract import error_response
from app.models.recording import ImportedProjectRecordingResponse
from app.models.research_project import ConditionCreateRequest, DesktopProjectEnsureRequest, ProjectCreateRequest, SessionCreateRequest, SubjectCreateRequest
from app.services.recording_import import EdfBdfImportService, RecordingImportError
from app.api.recording_uploads import publish_upload, stage_upload
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


@router.put("/desktop/{project_id}")
def ensure_desktop_project(project_id: str, payload: DesktopProjectEnsureRequest, request: Request):
    try:
        return _service(request).ensure_desktop_project(project_id, payload.name, payload.description).model_dump(mode="json")
    except ValueError as exc:
        return error_response(request, 422, "PROJECT_INVALID", str(exc))


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


@router.post("/{project_id}/recordings/import", status_code=status.HTTP_201_CREATED,
             response_model=ImportedProjectRecordingResponse)
async def import_project_recording(
    project_id: str,
    request: Request,
    file: UploadFile = File(...),
    subject_id: str | None = Form(None),
    new_subject_local_code: str | None = Form(None),
):
    staged = None
    try:
        importer = EdfBdfImportService(request.app.state.recording_service)
        staged = await stage_upload(request, file, importer)
        result = await publish_upload(request, lambda check: importer.publish_project(
            staged, project_id, subject_id=subject_id, new_subject_local_code=new_subject_local_code,
            check_cancelled=check,
        ))
    except RecordingImportError as exc:
        return error_response(request, exc.status_code, exc.code, str(exc))
    except OSError:
        return error_response(request, 507, "RECORDING_IMPORT_STORAGE_FAILED", "无法暂存录制文件，请检查磁盘空间和权限。")
    finally:
        if staged is not None:
            staged.path.unlink(missing_ok=True)
        await file.close()
    request.app.state.audit_service.record(
        "recording.import_project", str(getattr(request.state, "request_id", "unknown")),
        recording_id=result.recording.id, session_id=result.session_id,
        parameters={"project_id": result.project_id, "subject_id": result.subject_id,
                    "extension": result.recording.extension, "channel_count": len(result.recording.channels)},
    )
    recording = result.recording
    return ImportedProjectRecordingResponse(
        project_id=result.project_id, session_id=result.session_id, subject_id=result.subject_id,
        recording_id=recording.id, original_name=recording.original_name, extension=recording.extension,
        created_at=recording.created_at, sfreq=recording.sfreq, duration_s=recording.duration_s,
        channels=list(recording.channels), channel_types=list(recording.channel_types),
        channel_units=list(recording.channel_units), source_sha256=recording.source_sha256,
        file_size_bytes=recording.file_size_bytes,
    )


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

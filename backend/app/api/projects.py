"""Local research hierarchy HTTP resources."""

from collections.abc import Callable

from fastapi import APIRouter, File, Form, Request, UploadFile, status

from app.core.api_contract import error_response
from app.models.recording import ImportedProjectRecordingResponse
from app.models.research_project import (
    ConditionCreateRequest, DesktopProjectEnsureRequest, ProjectCreateRequest,
    ProjectRecordingDeletionResponse, Session, SessionCreateRequest, SessionRenameRequest, SubjectCreateRequest,
    Subject, SubjectDeletionResponse, SubjectResolutionResponse, SubjectSex,
)
from app.services.recordings.recording_import import EdfBdfImportService, RecordingImportError
from app.api.recording_uploads import publish_upload, stage_upload
from app.services.workspace.projects import ProjectConflictError, ProjectService
from app.services.workspace.research_subjects import SubjectBindingError


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


@router.post("/{project_id}/subjects/resolve", response_model=SubjectResolutionResponse)
def resolve_project_subject(project_id: str, payload: SubjectCreateRequest, request: Request):
    return _mutate(request, lambda: _service(request).resolve_project_subject(project_id, payload))


@router.patch("/{project_id}/subjects/{subject_id}", response_model=Subject)
def update_project_subject(project_id: str, subject_id: str, payload: SubjectCreateRequest, request: Request):
    return _mutate(request, lambda: _service(request).update_subject(project_id, subject_id, payload))


@router.delete("/{project_id}/subjects/{subject_id}", response_model=SubjectDeletionResponse)
def delete_project_subject(project_id: str, subject_id: str, request: Request):
    return _mutate(request, lambda: _service(request).delete_subject(project_id, subject_id))


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


@router.patch("/{project_id}/sessions/{session_id}", response_model=Session)
def rename_project_recording(project_id: str, session_id: str, payload: SessionRenameRequest, request: Request):
    try:
        metadata = (payload.subject_code, payload.subject_name) if {"subject_code", "subject_name"} & payload.model_fields_set else None
        binding = (payload.subject_id,) if "subject_id" in payload.model_fields_set else None
        age = (payload.age_at_recording_years,) if "age_at_recording_years" in payload.model_fields_set else None
        session = _service(request).rename_session(project_id, session_id, payload.label,
                                                   subject_metadata=metadata, subject_binding=binding, recording_age=age)
    except SubjectBindingError as exc:
        return error_response(request, exc.status_code, exc.code, str(exc))
    except KeyError:
        return error_response(request, 404, "RECORDING_SESSION_NOT_FOUND", "项目中的记录不存在")
    request.app.state.audit_service.record(
        "recording.rename_project", str(getattr(request.state, "request_id", "unknown")),
        recording_id=session.recording_id, session_id=session_id,
        parameters={"project_id": project_id},
    )
    return session


@router.delete("/{project_id}/sessions/{session_id}", response_model=ProjectRecordingDeletionResponse)
def delete_project_recording(project_id: str, session_id: str, request: Request):
    try:
        recording_id = _service(request).delete_imported_recording(project_id, session_id)
        recording_deleted = request.app.state.recording_service.delete_import_if_unreferenced(recording_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_SESSION_NOT_FOUND", "项目中的导入记录不存在")
    except ValueError as exc:
        return error_response(request, 422, "RECORDING_DELETE_UNSUPPORTED", str(exc))
    request.app.state.audit_service.record(
        "recording.delete_project", str(getattr(request.state, "request_id", "unknown")),
        recording_id=recording_id, session_id=session_id,
        parameters={"project_id": project_id, "recording_deleted": recording_deleted},
    )
    return ProjectRecordingDeletionResponse(
        session_id=session_id, recording_id=recording_id, recording_deleted=recording_deleted,
    )


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
    new_subject_name: str | None = Form(None),
    new_subject_sex: SubjectSex | None = Form(None),
    age_at_recording_years: int | None = Form(None, ge=0, le=150),
    channel_configuration_snapshot_json: str | None = Form(None),
    export_channel_identity_json: str | None = Form(None),
):
    staged = None
    try:
        importer = EdfBdfImportService(request.app.state.recording_service)
        staged = await stage_upload(request, file, importer)
        result = await publish_upload(request, lambda check: importer.publish_project(
            staged, project_id, subject_id=subject_id, new_subject_local_code=new_subject_local_code,
            new_subject_name=new_subject_name, new_subject_sex=new_subject_sex,
            age_at_recording_years=age_at_recording_years,
            channel_configuration_snapshot_json=channel_configuration_snapshot_json,
            export_channel_identity_json=export_channel_identity_json,
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
        channels=list(recording.channels), analysis_channels=list(recording.analysis_channels),
        channel_types=list(recording.channel_types),
        channel_units=list(recording.channel_units), source_sha256=recording.source_sha256,
        file_size_bytes=recording.file_size_bytes,
        import_channel_configuration_snapshot_json=recording.import_channel_configuration_snapshot_json,
        import_channel_column_mapping_json=recording.import_channel_column_mapping_json,
        import_acquisition_montage_snapshot_json=recording.import_acquisition_montage_snapshot_json,
        import_montage_configuration_snapshot_json=recording.import_montage_configuration_snapshot_json,
        import_montage_catalogue_snapshot_json=recording.import_montage_catalogue_snapshot_json,
    )


def _read(request: Request, operation: Callable[[], list]):
    try:
        return [item.model_dump(mode="json") for item in operation()]
    except KeyError:
        return error_response(request, 404, "PROJECT_NOT_FOUND", "项目不存在")


def _mutate(request: Request, operation: Callable[[], object]):
    try:
        return operation().model_dump(mode="json")
    except SubjectBindingError as exc:
        return error_response(request, exc.status_code, exc.code, str(exc))
    except ProjectConflictError as exc:
        code = "SUBJECT_CODE_CONFLICT" if "subject" in str(exc) else "CONDITION_CODE_CONFLICT"
        return error_response(request, 409, code, str(exc))
    except KeyError as exc:
        code = "RECORDING_NOT_FOUND" if "recording" in str(exc) else "PROJECT_REFERENCE_NOT_FOUND"
        return error_response(request, 404, code, "项目关联资源不存在")
    except ValueError as exc:
        return error_response(request, 422, "PROJECT_METADATA_INVALID", str(exc))

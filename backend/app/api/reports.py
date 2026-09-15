"""阅图报告快照 API；当前为本地开发版，尚未接入身份权限。"""

from typing import Any

from fastapi import APIRouter, Request, status
from pydantic import BaseModel, Field

from app.core.api_contract import error_response

router = APIRouter(prefix="/api/recordings", tags=["reports"])


class ReportSnapshotPayload(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    snapshot: dict[str, Any] = Field(default_factory=dict)


class ReportSnapshotResponse(BaseModel):
    id: str
    recording_id: str
    title: str
    created_at: str
    payload: dict[str, Any]


@router.post("/{recording_id}/reports", response_model=ReportSnapshotResponse, status_code=status.HTTP_201_CREATED)
def create_report(recording_id: str, payload: ReportSnapshotPayload, request: Request):
    try:
        request.app.state.recording_service.require_recording(recording_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    report = request.app.state.report_snapshot_service.create(recording_id, payload.title.strip(), payload.snapshot)
    request.app.state.audit_service.record(
        "report.create", str(getattr(request.state, "request_id", "unknown")), recording_id=recording_id,
        parameters={"title": payload.title.strip(), "snapshot_keys": sorted(payload.snapshot)},
    )
    return report


@router.get("/{recording_id}/reports", response_model=list[ReportSnapshotResponse])
def list_reports(recording_id: str, request: Request):
    try:
        request.app.state.recording_service.require_recording(recording_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    return request.app.state.report_snapshot_service.list_reports(recording_id)


@router.get("/{recording_id}/reports/{report_id}", response_model=ReportSnapshotResponse)
def get_report(recording_id: str, report_id: str, request: Request):
    try:
        request.app.state.recording_service.require_recording(recording_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    report = request.app.state.report_snapshot_service.get(recording_id, report_id)
    if report is None:
        return error_response(request, 404, "REPORT_NOT_FOUND", "报告快照不存在")
    return report

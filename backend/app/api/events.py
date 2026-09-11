"""人工事件标记 API；时间统一使用文件绝对秒数。"""

from fastapi import APIRouter, HTTPException, Query, Request, status
from pydantic import BaseModel, Field


router = APIRouter(prefix="/api/recordings", tags=["events"])


class EventMarkerPayload(BaseModel):
    time_s: float = Field(ge=0)
    label: str = Field(min_length=1, max_length=120)
    duration_s: float | None = Field(default=None, ge=0, le=86400)


class EventMarkerResponse(EventMarkerPayload):
    id: str
    recording_id: str
    created_at: str


def _recording(request: Request, recording_id: str):
    try:
        return request.app.state.recording_service.require_recording(recording_id)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


@router.get("/{recording_id}/events", response_model=list[EventMarkerResponse])
def list_events(recording_id: str, request: Request):
    _recording(request, recording_id)
    return request.app.state.event_marker_service.list_markers(recording_id)


@router.post("/{recording_id}/events", response_model=EventMarkerResponse, status_code=status.HTTP_201_CREATED)
def create_event(recording_id: str, payload: EventMarkerPayload, request: Request):
    recording = _recording(request, recording_id)
    if recording.duration_s is not None and payload.time_s > recording.duration_s:
        raise HTTPException(status_code=422, detail="事件时间不能超过文件时长")
    if payload.duration_s is not None and recording.duration_s is not None and payload.time_s + payload.duration_s > recording.duration_s:
        raise HTTPException(status_code=422, detail="事件结束时间不能超过文件时长")
    marker = request.app.state.event_marker_service.create(recording_id, payload.time_s, payload.label.strip(), payload.duration_s)
    request.app.state.audit_service.record(
        "event.create", str(getattr(request.state, "request_id", "unknown")), recording_id=recording_id,
        parameters={"time_s": payload.time_s, "label": payload.label.strip(), "duration_s": payload.duration_s},
    )
    return marker


@router.delete("/{recording_id}/events/{marker_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_event(recording_id: str, marker_id: str, request: Request):
    _recording(request, recording_id)
    if not request.app.state.event_marker_service.delete(recording_id, marker_id):
        raise HTTPException(status_code=404, detail="事件标记不存在")
    request.app.state.audit_service.record(
        "event.delete", str(getattr(request.state, "request_id", "unknown")), recording_id=recording_id,
        parameters={"marker_id": marker_id},
    )

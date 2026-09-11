from dataclasses import asdict
from typing import Optional

from fastapi import APIRouter, File, HTTPException, Query, Request, UploadFile, status
from pydantic import BaseModel

from app.models.recording import ChannelMapping, RecordingSummary
from app.services.recordings import RecordingService


router = APIRouter(prefix="/api/recordings", tags=["recordings"])


class ChannelMappingPayload(BaseModel):
    fz: str
    pz: str
    oz: str
    f3: Optional[str] = None
    f4: Optional[str] = None


def _serialize(recording: RecordingSummary) -> dict:
    data = asdict(recording)
    data["channels"] = list(recording.channels)
    return data


def _service(request: Request) -> RecordingService:
    return request.app.state.recording_service


@router.post("/import", status_code=status.HTTP_201_CREATED)
async def import_recording(request: Request, file: UploadFile = File(...)) -> dict:
    suffix = "." + file.filename.rsplit(".", 1)[-1].lower() if file.filename and "." in file.filename else ""
    try:
        recording = _service(request).create_imported_recording(file.filename or "recording", suffix, await file.read())
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return _serialize(recording)


@router.get("")
def list_recordings(request: Request) -> list[dict]:
    return [_serialize(recording) for recording in _service(request).list_recordings()]


@router.get("/{recording_id}")
def get_recording(recording_id: str, request: Request) -> dict:
    try:
        return _serialize(_service(request).require_recording(recording_id))
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


@router.get("/{recording_id}/preview")
def get_preview(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    window_s: float = Query(10.0, gt=0.0, le=60.0),
) -> dict:
    try:
        recording = _service(request).require_recording(recording_id)
        return _service(request).load_preview(recording, start_s=start_s, window_s=window_s)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=422, detail=f"无法读取波形预览：{exc}") from exc


@router.get("/{recording_id}/window")
def get_window(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    window_s: float = Query(10.0, gt=0.0, le=60.0),
    low_cut_hz: float = Query(0.5, gt=0.0),
    high_cut_hz: float = Query(70.0, gt=0.0),
    notch_hz: Optional[float] = Query(None),
    reference: str = Query("original"),
    channels: Optional[str] = Query(None),
) -> dict:
    """阅图模式：拖动时间轴时只读取并返回当前完整窗口。"""
    try:
        recording = _service(request).require_recording(recording_id)
        requested_channels = [item.strip() for item in channels.split(",") if item.strip()] if channels else None
        return _service(request).load_window(
            recording,
            start_s=start_s,
            window_s=window_s,
            low_cut_hz=low_cut_hz,
            high_cut_hz=high_cut_hz,
            notch_hz=notch_hz,
            reference=reference,
            channels=requested_channels,
        )
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=422, detail=f"无法读取波形窗口：{exc}") from exc


@router.put("/{recording_id}/mapping")
def save_mapping(recording_id: str, payload: ChannelMappingPayload, request: Request) -> dict:
    mapping = ChannelMapping(**payload.model_dump())
    try:
        recording = _service(request).update_mapping(recording_id, mapping)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return _serialize(recording)

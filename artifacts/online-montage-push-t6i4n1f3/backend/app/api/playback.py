from fastapi import APIRouter, Request, WebSocket, WebSocketDisconnect, status
from pydantic import BaseModel

from app.services.waveform_playback import WaveformPlaybackService
from app.models.montage import CustomMontageChannelPayload
from app.core.api_contract import error_response


router = APIRouter(tags=["playback"])


class WaveformPlaybackControl(BaseModel):
    action: str
    speed: float | None = None
    position_s: float | None = None
    low_cut_hz: float | None = None
    high_cut_hz: float | None = None
    notch_hz: float | None = None
    baseline_stabilization: bool | None = None
    channels: list[str] | None = None


class WaveformPlaybackCreate(BaseModel):
    channels: list[str] | None = None
    montage: str = "original"
    average_exclude: list[str] | None = None
    custom_montage: list[CustomMontageChannelPayload] | None = None


def _record_audit(request: Request, action: str, *, recording_id: str | None = None, session_id: str | None = None,
                  parameters: dict[str, object] | None = None) -> None:
    request.app.state.audit_service.record(
        action, str(getattr(request.state, "request_id", "unknown")), recording_id=recording_id,
        session_id=session_id, parameters=parameters,
    )


@router.post("/api/recordings/{recording_id}/waveform-playback", status_code=status.HTTP_201_CREATED)
def create_waveform_playback(
    recording_id: str,
    request: Request,
    payload: WaveformPlaybackCreate | None = None,
) -> dict:
    try:
        session: WaveformPlaybackService = request.app.state.waveform_playback_service
        requested_channels = payload.channels if payload else None
        montage_id = payload.montage if payload else "original"
        average_exclude = payload.average_exclude if payload else None
        custom_montage = [row.model_dump() for row in payload.custom_montage] if payload and payload.custom_montage else None
        if requested_channels is not None and not requested_channels:
            raise ValueError("至少选择一个有效显示通道")
        if montage_id == "original":
            if average_exclude is None:
                created = session.create(recording_id, requested_channels=requested_channels)
            else:
                created = session.create(recording_id, requested_channels=requested_channels, average_exclude=average_exclude, custom_montage=custom_montage)
        else:
            created = session.create(recording_id, requested_channels=requested_channels, montage_id=montage_id, average_exclude=average_exclude, custom_montage=custom_montage)
        _record_audit(request, "waveform.playback_create", recording_id=recording_id, session_id=created.id, parameters={
            "channels": requested_channels, "montage": montage_id, "average_exclude": average_exclude or [], "custom_montage": custom_montage or [],
        })
        return {
            "session_id": created.id,
            "recording_id": recording_id,
            "status": created.status,
            "websocket_url": f"/api/waveform-playback/{created.id}/events",
        }
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "WAVEFORM_PLAYBACK_CREATE_INVALID", str(exc))


@router.post("/api/waveform-playback/{session_id}/control")
def control_waveform_playback(session_id: str, payload: WaveformPlaybackControl, request: Request) -> dict:
    try:
        service: WaveformPlaybackService = request.app.state.waveform_playback_service
        values = payload.model_dump(exclude_none=True)
        if "notch_hz" in payload.model_fields_set:
            values["notch_hz"] = payload.notch_hz
        values.pop("action", None)
        session = service.require(session_id)
        response = session.control(payload.action, **values)
        _record_audit(request, "waveform.playback_control", recording_id=getattr(session, "recording_id", None), session_id=session_id,
                      parameters={"action": payload.action, **values})
        return response
    except KeyError:
        return error_response(request, 404, "WAVEFORM_PLAYBACK_SESSION_NOT_FOUND", "波形回放会话不存在")
    except ValueError as exc:
        return error_response(request, 422, "PLAYBACK_CONTROL_INVALID", str(exc))


@router.websocket("/api/waveform-playback/{session_id}/events")
async def waveform_playback_events(session_id: str, websocket: WebSocket) -> None:
    await websocket.accept()
    service: WaveformPlaybackService = websocket.app.state.waveform_playback_service
    try:
        session = service.require(session_id)
        while True:
            message = await session.next_message()
            if isinstance(message, bytes):
                await websocket.send_bytes(message)
            else:
                await websocket.send_json(message)
    except KeyError:
        await websocket.send_json({"type": "error", "detail": "波形回放会话不存在"})
        await websocket.close(code=4404)
    except WebSocketDisconnect:
        return

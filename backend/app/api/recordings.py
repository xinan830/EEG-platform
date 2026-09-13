from dataclasses import asdict
import json
from typing import Optional

from fastapi import APIRouter, File, HTTPException, Query, Request, UploadFile, status
from pydantic import BaseModel, TypeAdapter, ValidationError

from app.models.recording import ChannelMapping, RecordingSummary
from app.models.analysis_config import AnalysisConfigRequest
from app.models.montage import CustomMontageChannelPayload
from app.services.recordings import RecordingService
from app.services.montage import build_montage, describe_montages, montage_formulas


router = APIRouter(prefix="/api/recordings", tags=["recordings"])


class ChannelMappingPayload(BaseModel):
    fz: str
    pz: str
    oz: str
    f3: Optional[str] = None
    f4: Optional[str] = None


def _custom_montage(value: str | None) -> list[dict[str, object]] | None:
    if value is None:
        return None
    try:
        payload = json.loads(value)
    except json.JSONDecodeError as exc:
        raise ValueError("自定义 Montage 定义不是有效 JSON") from exc
    try:
        rows = TypeAdapter(list[CustomMontageChannelPayload]).validate_python(payload)
    except ValidationError as exc:
        raise ValueError("自定义 Montage 定义格式不正确") from exc
    return [row.model_dump() for row in rows]


def _serialize(recording: RecordingSummary) -> dict:
    data = asdict(recording)
    data["channels"] = list(recording.channels)
    return data


def _service(request: Request) -> RecordingService:
    return request.app.state.recording_service


def _record_audit(request: Request, action: str, recording_id: str, parameters: dict[str, object]) -> None:
    request.app.state.audit_service.record(
        action, str(getattr(request.state, "request_id", "unknown")), recording_id=recording_id, parameters=parameters,
    )


@router.post("/import", status_code=status.HTTP_201_CREATED)
async def import_recording(request: Request, file: UploadFile = File(...)) -> dict:
    suffix = "." + file.filename.rsplit(".", 1)[-1].lower() if file.filename and "." in file.filename else ""
    try:
        recording = _service(request).create_imported_recording(file.filename or "recording", suffix, await file.read())
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    _record_audit(request, "recording.import", recording.id, {"extension": recording.extension, "channel_count": len(recording.channels)})
    return _serialize(recording)


@router.get("")
def list_recordings(request: Request) -> list[dict]:
    return [_serialize(recording) for recording in _service(request).list_recordings()]


@router.get("/{recording_id}/montages")
def list_montages(recording_id: str, request: Request) -> dict:
    try:
        recording = _service(request).require_recording(recording_id)
        return {"recording_id": recording_id, "montages": describe_montages(list(recording.channels))}
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


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
    baseline_stabilization: bool = Query(False),
    reference: str = Query("original"),
    montage: Optional[str] = Query(None),
    average_exclude: Optional[str] = Query(None),
    custom_montage: Optional[str] = Query(None),
    channels: Optional[str] = Query(None),
) -> dict:
    """阅图模式：拖动时间轴时只读取并返回当前完整窗口。"""
    try:
        recording = _service(request).require_recording(recording_id)
        requested_channels = [item.strip() for item in channels.split(",") if item.strip()] if channels else None
        payload = _service(request).load_window(
            recording,
            start_s=start_s,
            window_s=window_s,
            low_cut_hz=low_cut_hz,
            high_cut_hz=high_cut_hz,
            notch_hz=notch_hz,
            baseline_stabilization=baseline_stabilization,
            reference=reference,
            channels=requested_channels,
            montage=montage,
            average_exclude=[item.strip() for item in average_exclude.split(",") if item.strip()] if average_exclude else None,
            custom_montage=_custom_montage(custom_montage),
        )
        _record_audit(request, "waveform.window", recording_id, {
            "start_s": start_s, "window_s": window_s, "low_cut_hz": low_cut_hz, "high_cut_hz": high_cut_hz,
            "notch_hz": notch_hz, "baseline_stabilization": baseline_stabilization, "reference": reference, "montage": montage, "channels": requested_channels,
            "average_exclude": payload["settings"].get("average_exclude", []),
            "custom_montage": payload["settings"].get("custom_montage", []),
        })
        return payload
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=422, detail=f"无法读取波形窗口：{exc}") from exc


@router.get("/{recording_id}/spectrum")
def get_spectrum(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    window_s: float = Query(30.0, ge=4.0, le=120.0),
    channels: Optional[str] = Query(None),
) -> dict:
    """Return the frozen offline-spectral-v3 PSD and band-power contract."""
    try:
        recording = _service(request).require_recording(recording_id)
        requested = [item.strip() for item in channels.split(",") if item.strip()] if channels else None
        payload = _service(request).load_spectrum(recording, start_s=start_s, window_s=window_s, channels=requested)
        _record_audit(request, "analysis.spectrum", recording_id, {"start_s": start_s, "window_s": window_s, "channels": requested or list(recording.channels), "algorithm_version": payload["algorithm_version"]})
        return payload
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=422, detail=f"无法计算频谱：{exc}") from exc


@router.get("/{recording_id}/spectrogram")
def get_spectrogram(recording_id: str, request: Request, start_s: float = Query(0.0, ge=0.0), window_s: float = Query(30.0, ge=4.0, le=120.0), channels: Optional[str] = Query(None)) -> dict:
    try:
        recording = _service(request).require_recording(recording_id)
        requested = [item.strip() for item in channels.split(",") if item.strip()] if channels else None
        payload = _service(request).load_spectrogram(recording, start_s, window_s, requested)
        _record_audit(request, "analysis.spectrogram", recording_id, {"start_s": start_s, "window_s": window_s, "channels": requested or list(recording.channels)})
        return payload
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/{recording_id}/spectrum/configured")
def get_configured_spectrum(recording_id: str, request: Request, config: AnalysisConfigRequest) -> dict:
    """Return configurable timing/channel analysis backed by frozen v3 math."""
    try:
        recording = _service(request).require_recording(recording_id)
        payload = _service(request).load_configured_spectrum(recording, config)
        _record_audit(request, "analysis.spectrum.configured", recording_id, {
            "requested_config": config.model_dump(mode="json"),
            "analysis_config_hash": payload["analysis_config_hash"],
        })
        return payload
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/{recording_id}/spectrogram/configured")
def get_configured_spectrogram(recording_id: str, request: Request, config: AnalysisConfigRequest) -> dict:
    """Return configurable timing/channel spectrogram data."""
    try:
        recording = _service(request).require_recording(recording_id)
        payload = _service(request).load_configured_spectrogram(recording, config)
        _record_audit(request, "analysis.spectrogram.configured", recording_id, {"requested_config": config.model_dump(mode="json"), "analysis_config_hash": payload["analysis_config_hash"]})
        return payload
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.get("/{recording_id}/algorithm-check")
def algorithm_check(
    recording_id: str,
    request: Request,
    time_s: float = Query(0.0, ge=0.0),
    low_cut_hz: float = Query(0.5, gt=0.0),
    high_cut_hz: float = Query(70.0, gt=0.0),
    notch_hz: Optional[float] = Query(None),
    baseline_stabilization: bool = Query(False),
    reference: str = Query("original"),
    montage: Optional[str] = Query(None),
    average_exclude: Optional[str] = Query(None),
    custom_montage: Optional[str] = Query(None),
    channels: Optional[str] = Query(None),
) -> dict:
    """Return one processed sample plus its montage formulas for diagnostics."""
    try:
        service = _service(request)
        recording = service.require_recording(recording_id)
        requested = [item.strip() for item in channels.split(",") if item.strip()] if channels else None
        payload = service.load_window(
            recording, start_s=time_s, window_s=0.1, low_cut_hz=low_cut_hz,
            high_cut_hz=high_cut_hz, notch_hz=notch_hz, baseline_stabilization=baseline_stabilization, reference=reference,
            channels=requested, montage=montage,
            average_exclude=[item.strip() for item in average_exclude.split(",") if item.strip()] if average_exclude else None,
            custom_montage=_custom_montage(custom_montage),
        )
        definition = build_montage(
            payload["settings"]["montage"], list(recording.channels), requested,
            payload["settings"].get("average_exclude"), payload["settings"].get("custom_montage"),
        )
        values = {name: numbers[0] if numbers else None for name, numbers in payload["channels"].items()}
        response = {
            "time_s": payload["elapsed_s"][0] if payload["elapsed_s"] else time_s,
            "montage": payload["settings"]["montage"],
            "montage_label": definition.label,
            "average_participants": len(definition.required_channels) if definition.id != "average" else len(definition.required_channels) - len(definition.excluded_channels),
            "formulas": montage_formulas(definition),
            "values_uv": values,
            "settings": payload["settings"],
        }
        _record_audit(request, "waveform.algorithm_check", recording_id, {
            "time_s": time_s, "montage": response["montage"], "channels": requested,
            "low_cut_hz": low_cut_hz, "high_cut_hz": high_cut_hz, "notch_hz": notch_hz, "baseline_stabilization": baseline_stabilization,
            "average_exclude": payload["settings"].get("average_exclude", []),
            "custom_montage": payload["settings"].get("custom_montage", []),
        })
        return response
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=422, detail=f"无法执行算法检验：{exc}") from exc


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

import numpy as np
from fastapi import APIRouter, Query, Request, Response, status
from pydantic import BaseModel, Field

from app.core.api_contract import error_response
from app.services.live_filter import LiveFilterService


router = APIRouter(prefix="/api/live-filters", tags=["live-filters"])


class LiveFilterSessionCreate(BaseModel):
    session_id: str = Field(min_length=1)
    sampling_rate_hz: int = Field(gt=0)
    channel_count: int = Field(gt=0)
    eeg_channel_indexes: list[int] = Field(min_length=1)
    low_cut_hz: float = Field(gt=0)
    high_cut_hz: float = Field(gt=0)
    notch_hz: float | None = None
    warmup_sample_count: int = Field(default=0, ge=0)
    warmup_values_v: list[float] = Field(default_factory=list)
    return_warmup_values: bool = False


class LiveFilterBatch(BaseModel):
    sample_count: int = Field(gt=0)
    values_v: list[float]


def _service(request: Request) -> LiveFilterService:
    return request.app.state.live_filter_service


@router.post("/sessions", status_code=status.HTTP_201_CREATED)
def create_session(payload: LiveFilterSessionCreate, request: Request) -> dict:
    try:
        expected_warmup_values = payload.warmup_sample_count * payload.channel_count
        if len(payload.warmup_values_v) != expected_warmup_values:
            raise ValueError("warmup values length does not match warmup_sample_count and channel_count")
        session = _service(request).create(
            **payload.model_dump(exclude={"warmup_sample_count", "warmup_values_v", "return_warmup_values"})
        )
        warmup_values_v: list[float] = []
        if payload.warmup_sample_count:
            filtered_warmup_values = _service(request).process(
                payload.session_id,
                payload.warmup_values_v,
                payload.warmup_sample_count,
            )
            if payload.return_warmup_values:
                warmup_values_v = filtered_warmup_values
        return {
            "session_id": payload.session_id,
            "low_cut_hz": session.low_cut_hz,
            "high_cut_hz": session.high_cut_hz,
            "notch_hz": session.notch_hz,
            "warmup_sample_count": payload.warmup_sample_count,
            "warmup_values_v": warmup_values_v,
            "unit": "V",
            "filter_contract": _service(request).contract,
        }
    except ValueError as exc:
        return error_response(request, 422, "LIVE_FILTER_CONFIG_INVALID", str(exc))


@router.post("/sessions/{session_id}/batches")
def filter_batch(session_id: str, payload: LiveFilterBatch, request: Request) -> dict:
    try:
        return {"session_id": session_id, "sample_count": payload.sample_count, "values_v": _service(request).process(session_id, payload.values_v, payload.sample_count), "unit": "V"}
    except KeyError:
        return error_response(request, 404, "LIVE_FILTER_SESSION_NOT_FOUND", "实时滤波会话不存在")
    except ValueError as exc:
        return error_response(request, 422, "LIVE_FILTER_BATCH_INVALID", str(exc))


@router.post("/sessions/{session_id}/batches/binary")
async def filter_batch_binary(
    session_id: str,
    request: Request,
    sample_count: int = Query(gt=0),
) -> Response:
    """Float64 little-endian batch IPC for the desktop live-display path.

    This endpoint deliberately transports only display-filter data. It does
    not persist recordings or change the raw float64 acquisition contract.
    """
    try:
        payload = await request.body()
        if len(payload) % np.dtype("<f8").itemsize:
            raise ValueError("binary batch payload is not aligned to float64")
        values = np.frombuffer(payload, dtype="<f8")
        filtered = _service(request).process_array(session_id, values, sample_count)
        return Response(
            content=np.asarray(filtered, dtype="<f8").tobytes(),
            media_type="application/vnd.brain-platform.float64",
            headers={"X-Sample-Count": str(sample_count), "X-Unit": "V"},
        )
    except KeyError:
        return error_response(request, 404, "LIVE_FILTER_SESSION_NOT_FOUND", "实时滤波会话不存在")
    except ValueError as exc:
        return error_response(request, 422, "LIVE_FILTER_BATCH_INVALID", str(exc))


@router.post("/sessions/{session_id}/warmup/binary", status_code=status.HTTP_204_NO_CONTENT)
async def warmup_filter_binary(
    session_id: str,
    request: Request,
    sample_count: int = Query(gt=0),
) -> Response:
    """Advance a new display-filter session without returning warm-up samples.

    Filter changes use this endpoint off the live display path. Keeping the
    payload as float64 avoids a very large JSON allocation and the resulting
    UI/display stall at high sampling rates.
    """
    try:
        payload = await request.body()
        if len(payload) % np.dtype("<f8").itemsize:
            raise ValueError("binary warmup payload is not aligned to float64")
        values = np.frombuffer(payload, dtype="<f8")
        _service(request).process_array(session_id, values, sample_count)
        return Response(
            status_code=status.HTTP_204_NO_CONTENT,
            headers={"X-Warmup-Sample-Count": str(sample_count), "X-Unit": "V"},
        )
    except KeyError:
        return error_response(request, 404, "LIVE_FILTER_SESSION_NOT_FOUND", "实时滤波会话不存在")
    except ValueError as exc:
        return error_response(request, 422, "LIVE_FILTER_BATCH_INVALID", str(exc))


@router.delete("/sessions/{session_id}", status_code=status.HTTP_204_NO_CONTENT)
def close_session(session_id: str, request: Request) -> None:
    _service(request).close(session_id)

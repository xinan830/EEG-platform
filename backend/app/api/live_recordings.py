import json

import numpy as np
from fastapi import APIRouter, Query, Request, Response, status
from pydantic import BaseModel, Field

from app.core.api_contract import error_response
from app.services.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessService


router = APIRouter(prefix="/api/live-recordings", tags=["live-recordings"])


class LiveAccessSessionCreate(BaseModel):
    session_id: str = Field(min_length=1)
    stream_revision: str = Field(min_length=1)
    schema_revision: str = Field(min_length=1)
    sampling_rate_hz: float = Field(gt=0)
    channel_names: list[str] = Field(min_length=1)
    channel_units: list[str] = Field(min_length=1)
    capacity_samples: int = Field(default=30_000, gt=0, le=2_000_000)


def _service(request: Request) -> LiveRecordingAccessService:
    return request.app.state.live_recording_access_service


def _error(request: Request, exc: LiveRecordingAccessError):
    return error_response(request, 409 if exc.code in {"LIVE_SESSION_CAPACITY", "LIVE_BATCH_OVERLAP"} else 422, exc.code, str(exc))


@router.post("/sessions", status_code=status.HTTP_201_CREATED)
def create_session(payload: LiveAccessSessionCreate, request: Request) -> dict:
    try:
        session = _service(request).create(**payload.model_dump())
        return {
            "source_identity": session.identity.__dict__,
            "sampling_rate_hz": session.sampling_rate_hz,
            "channel_names": list(session.channel_names),
            "channel_units": list(session.channel_units),
            "range": session.range_snapshot().__dict__,
        }
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.post("/sessions/{session_id}/batches/binary")
async def append_batch_binary(
    session_id: str,
    request: Request,
    first_sample_counter: int = Query(ge=0),
    sample_count: int = Query(gt=0),
) -> dict:
    try:
        payload = await request.body()
        if len(payload) % np.dtype("<f8").itemsize:
            raise LiveRecordingAccessError("LIVE_BATCH_ALIGNMENT_INVALID", "实时批次不是完整的 float64 字节序列。")
        values = np.frombuffer(payload, dtype="<f8")
        session = _service(request).require(session_id)
        snapshot = session.append(first_sample_counter, values, sample_count)
        return {"session_id": session_id, "range": snapshot.__dict__}
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.get("/sessions/{session_id}/range")
def get_range(session_id: str, request: Request) -> dict:
    try:
        return {"session_id": session_id, "range": _service(request).require(session_id).range_snapshot().__dict__}
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.get("/sessions/{session_id}/read/binary")
def read_samples_binary(
    session_id: str,
    request: Request,
    start_sample: int = Query(ge=0),
    end_sample: int = Query(gt=0),
    channels: list[str] = Query(min_length=1),
) -> Response:
    try:
        block = _service(request).require(session_id).read_samples(start_sample, end_sample, channels)
        unavailable = [interval.__dict__ for interval in block.unavailable_intervals]
        return Response(
            content=np.asarray(block.values_v, dtype="<f8").tobytes(),
            media_type="application/vnd.brain-platform.float64",
            headers={
                "X-Start-Sample": str(block.start_sample),
                "X-End-Sample": str(block.end_sample),
                "X-Sampling-Rate-Hz": str(block.sampling_rate_hz),
                "X-Channel-Names": json.dumps(block.channel_names, ensure_ascii=False),
                "X-Channel-Units": json.dumps(block.channel_units, ensure_ascii=False),
                "X-Unavailable-Intervals": json.dumps(unavailable, ensure_ascii=False),
                "X-Unit": "V" if all(unit == "V" for unit in block.channel_units) else "per-channel",
            },
        )
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.delete("/sessions/{session_id}", status_code=status.HTTP_204_NO_CONTENT)
def close_session(session_id: str, request: Request) -> None:
    _service(request).close(session_id)

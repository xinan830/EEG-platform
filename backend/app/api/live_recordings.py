import json

import numpy as np
from fastapi import APIRouter, Query, Request, Response, status
from fastapi.responses import JSONResponse
from typing import Literal

from pydantic import BaseModel, Field, JsonValue

from app.core.api_contract import error_response
from app.services.live_analysis.live_recording_access import LiveRecordingAccessError, LiveRecordingAccessService
from app.services.live_analysis.live_analysis_contracts import LiveAnalysisEnvelope
from app.services.live_analysis.live_analysis_coordinator import LiveAnalysisCoordinator


router = APIRouter(prefix="/api/live-recordings", tags=["live-recordings"])


class LiveAccessSessionCreate(BaseModel):
    session_id: str = Field(min_length=1)
    stream_revision: str = Field(min_length=1)
    schema_revision: str = Field(min_length=1)
    sampling_rate_hz: float = Field(gt=0)
    channel_names: list[str] = Field(min_length=1)
    channel_units: list[str] = Field(min_length=1)
    capacity_samples: int = Field(default=30_000, gt=0, le=2_000_000)


class LiveAlgorithmStartRequest(BaseModel):
    model_config = {"extra": "forbid"}

    schema_version: Literal["live-analysis-config-v1"] = "live-analysis-config-v1"
    config: dict[str, JsonValue]


class LiveMontageUpdate(BaseModel):
    model_config = {"extra": "forbid"}

    schema_version: Literal["live-montage-v1"] = "live-montage-v1"
    revision: int = Field(gt=0)
    effective_from_sample_counter: int = Field(ge=0)
    fingerprint: str = Field(min_length=1)
    profile: dict[str, JsonValue]


class LiveSampleAcknowledgement(BaseModel):
    type: Literal["live_samples_accepted"] = "live_samples_accepted"
    schema_version: Literal["live-sample-ack-v2"] = "live-sample-ack-v2"
    session_id: str
    range: dict[str, int | None]
    updates: list[LiveAnalysisEnvelope]


def _service(request: Request) -> LiveRecordingAccessService:
    return request.app.state.live_recording_access_service


def _analysis(request: Request) -> LiveAnalysisCoordinator:
    return request.app.state.live_analysis_coordinator


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


@router.post("/sessions/{session_id}/batches/binary", response_model=LiveSampleAcknowledgement)
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
        updates = _analysis(request).on_samples(session_id, session)
        return JSONResponse(
            content=LiveSampleAcknowledgement(
                session_id=session_id, range=snapshot.__dict__, updates=updates,
            ).model_dump(mode="json")
        )
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.get("/sessions/{session_id}/range")
def get_range(session_id: str, request: Request) -> dict:
    try:
        return {"session_id": session_id, "range": _service(request).require(session_id).range_snapshot().__dict__}
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.put("/sessions/{session_id}/montage")
def update_montage(session_id: str, payload: LiveMontageUpdate, request: Request) -> dict:
    try:
        session = _service(request).require(session_id)
        session.set_montage(payload.profile, payload.revision, payload.effective_from_sample_counter)
        _analysis(request).close_session(session_id)
        return {"session_id": session_id, "revision": payload.revision,
                "effective_from_sample_counter": payload.effective_from_sample_counter,
                "fingerprint": payload.fingerprint, "channel_names": list(session.channel_names)}
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
    _analysis(request).close_session(session_id)
    _service(request).close(session_id)


@router.post("/sessions/{session_id}/algorithms/{algorithm_id}", status_code=status.HTTP_201_CREATED,
    response_model=LiveAnalysisEnvelope)
def start_algorithm(session_id: str, algorithm_id: str, payload: LiveAlgorithmStartRequest, request: Request):
    try:
        return _analysis(request).start(session_id, _service(request).require(session_id), algorithm_id, payload.config)
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.get("/sessions/{session_id}/algorithms/{algorithm_id}", response_model=LiveAnalysisEnvelope)
def latest_algorithm(session_id: str, algorithm_id: str, request: Request):
    try:
        _service(request).require(session_id)
        result = _analysis(request).latest(session_id, algorithm_id)
        if result is None:
            raise LiveRecordingAccessError("LIVE_ALGORITHM_NOT_STARTED", "实时算法尚未启动或已停止。")
        return result
    except LiveRecordingAccessError as exc:
        return _error(request, exc)


@router.delete("/sessions/{session_id}/algorithms/{algorithm_id}", status_code=status.HTTP_204_NO_CONTENT)
def stop_algorithm(session_id: str, algorithm_id: str, request: Request):
    try:
        _service(request).require(session_id)
        _analysis(request).stop(session_id, algorithm_id)
    except LiveRecordingAccessError as exc:
        return _error(request, exc)

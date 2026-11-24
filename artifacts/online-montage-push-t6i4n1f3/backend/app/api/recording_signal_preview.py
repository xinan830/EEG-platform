"""Reusable display-only sample window contract, distinct from algorithm preprocessing."""
from fastapi import APIRouter, Request
from pydantic import BaseModel, ConfigDict, Field
from app.core.api_contract import error_response
from app.services.recording_signal_preview import signal_preview
from app.algorithm_runtime.errors import DynamicWindowResourceExceededError

router = APIRouter(prefix="/api/recordings", tags=["recordings"])


class SignalPreviewRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    start_s: float = Field(ge=0, allow_inf_nan=False)
    window_s: float = Field(gt=0, le=30, allow_inf_nan=False)
    montage_id: str | None = None
    channels: list[str] = Field(min_length=1, max_length=32)
    max_points: int = Field(default=4000, ge=100, le=8000)
    low_hz: float = Field(default=1.0, gt=0, allow_inf_nan=False)
    high_hz: float = Field(default=30.0, gt=0, allow_inf_nan=False)
    notch_hz: float | None = Field(default=None, gt=0, allow_inf_nan=False)


class SignalPreviewChannelExtrema(BaseModel):
    minimum_uv: list[float]
    maximum_uv: list[float]


class SignalPreviewResponse(BaseModel):
    recording_id: str
    montage_id: str | None
    unit: str
    filter: str
    sfreq: float
    start_s: float
    end_s: float
    elapsed_s: list[float]
    channels: dict[str, list[float]]
    display_stride_samples: int
    low_hz: float
    high_hz: float
    notch_hz: float | None
    extrema_elapsed_s: list[float] | None = None
    channel_extrema: dict[str, SignalPreviewChannelExtrema] | None = None


@router.post("/{recording_id}/signal-preview", response_model=SignalPreviewResponse)
def preview(recording_id: str, payload: SignalPreviewRequest, request: Request) -> dict:
    try:
        return signal_preview(
            request.app.state.recording_service,
            recording_id,
            **payload.model_dump(),
            request_id=str(getattr(request.state, "request_id", "unknown")),
        )
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as error:
        return error_response(request, 422, "SIGNAL_PREVIEW_INVALID", str(error))
    except DynamicWindowResourceExceededError as error:
        return error_response(request, 422, "SIGNAL_PREVIEW_RESOURCE_EXCEEDED", str(error))

"""Custom PSD/RBP projections from paired saved dynamic windows."""

import logging
from time import perf_counter

from fastapi import APIRouter, Request
from starlette.responses import Response

from app.core.api_contract import error_response
from app.models.custom_frequency_inspection import (
    CustomFrequencyBinsResponse,
    CustomFrequencyRangeRequest,
    CustomFrequencyRangeResponse,
    CustomFrequencyWindowPair,
)
from app.services.result_scalar_inputs import ResultProjectionUnavailable

logger = logging.getLogger("brain_platform.custom_frequency")
router = APIRouter(prefix="/api/analysis-windows/custom-frequency", tags=["custom-frequency"])


@router.post("/bins", response_model=CustomFrequencyBinsResponse)
def project_bins(payload: CustomFrequencyWindowPair, request: Request):
    try:
        started = perf_counter()
        result = request.app.state.custom_frequency_inspection_service.read_bins(payload)
        service_ms = (perf_counter() - started) * 1000
        encode_started = perf_counter()
        response = Response(content=result.model_dump_json(), media_type="application/json")
        logger.info("custom_frequency_bins request_id=%s psd_window_id=%s rbp_window_id=%s channels=%d bins=%d service_ms=%.3f response_encode_ms=%.3f response_bytes=%d total_ms=%.1f",
            getattr(request.state, "request_id", "unknown"), payload.psd_window_id, payload.rbp_window_id,
            len(result.channels), len(result.frequency_hz), service_ms, (perf_counter() - encode_started) * 1000,
            len(response.body), (perf_counter() - started) * 1000)
        return response
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except ResultProjectionUnavailable as exc:
        return error_response(request, 409, exc.code, exc.message)
    except ValueError as exc:
        return error_response(request, 409, "CUSTOM_FREQUENCY_SOURCE_UNAVAILABLE", str(exc))


@router.post("/range", response_model=CustomFrequencyRangeResponse)
def project_range(payload: CustomFrequencyRangeRequest, request: Request):
    try:
        started = perf_counter()
        result = request.app.state.custom_frequency_inspection_service.read_range(payload)
        service_ms = (perf_counter() - started) * 1000
        encode_started = perf_counter()
        response = Response(content=result.model_dump_json(), media_type="application/json")
        logger.info("custom_frequency_range request_id=%s psd_window_id=%s rbp_window_id=%s low_hz=%.6f high_hz=%.6f channels=%d service_ms=%.3f response_encode_ms=%.3f response_bytes=%d total_ms=%.1f",
            getattr(request.state, "request_id", "unknown"), payload.psd_window_id, payload.rbp_window_id,
            payload.low_hz, payload.high_hz, len(result.channels), service_ms,
            (perf_counter() - encode_started) * 1000, len(response.body), (perf_counter() - started) * 1000)
        return response
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except ResultProjectionUnavailable as exc:
        return error_response(request, 422 if exc.code == "CUSTOM_FREQUENCY_RANGE_UNSUPPORTED" else 409,
                              exc.code, exc.message)
    except ValueError as exc:
        return error_response(request, 409, "CUSTOM_FREQUENCY_SOURCE_UNAVAILABLE", str(exc))

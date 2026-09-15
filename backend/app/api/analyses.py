from dataclasses import asdict

from fastapi import APIRouter, Request, status

from app.core.api_contract import error_response
from app.services.analysis import AnalysisService


router = APIRouter(tags=["analyses"])


def _service(request: Request) -> AnalysisService:
    # Reuse the application-owned queue/facade.  Constructing a fresh
    # RunService here would bypass the configured artifact root and make test
    # or local dependency overrides invisible to the compatibility endpoint.
    return AnalysisService(
        request.app.state.recording_service,
        run_service=request.app.state.run_service,
    )


@router.post("/api/recordings/{recording_id}/analysis", status_code=status.HTTP_201_CREATED)
def create_analysis(recording_id: str, request: Request) -> dict:
    try:
        summary = _service(request).create_analysis(recording_id)
        request.app.state.audit_service.record(
            "analysis.create", str(getattr(request.state, "request_id", "unknown")),
            recording_id=recording_id, parameters={"algorithm_version": summary.algorithm_version},
        )
        return asdict(summary)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except RuntimeError as exc:
        return error_response(request, 409, "ANALYSIS_MAPPING_REQUIRED", str(exc))
    except ValueError as exc:
        return error_response(request, 422, "ANALYSIS_REQUEST_INVALID", str(exc))


@router.get("/api/analyses/{analysis_id}")
def get_analysis(analysis_id: str, request: Request) -> dict:
    try:
        return _service(request).get_result(analysis_id)
    except KeyError:
        return error_response(request, 404, "ANALYSIS_NOT_FOUND", "分析结果不存在")
    except ValueError as exc:
        return error_response(request, 409, "ANALYSIS_NOT_READY", str(exc))

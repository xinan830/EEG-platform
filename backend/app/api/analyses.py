from fastapi import APIRouter, Request

from app.core.api_contract import error_response
from app.services.analysis import AnalysisService


router = APIRouter(tags=["analyses"])


def _service(request: Request) -> AnalysisService:
    # Reuse the application-owned queue/facade.  Constructing a fresh
    # RunService here would bypass the configured artifact root and make test
    # or local dependency overrides invisible to the compatibility endpoint.
    return AnalysisService(
        request.app.state.recording_service,
        database_path=request.app.state.recording_service.database_path,
        run_service=request.app.state.run_service,
    )


@router.get("/api/analyses/{analysis_id}")
def get_analysis(analysis_id: str, request: Request) -> dict:
    try:
        return _service(request).get_result(analysis_id)
    except KeyError:
        return error_response(request, 404, "ANALYSIS_NOT_FOUND", "分析结果不存在")
    except ValueError as exc:
        return error_response(request, 409, "ANALYSIS_NOT_READY", str(exc))

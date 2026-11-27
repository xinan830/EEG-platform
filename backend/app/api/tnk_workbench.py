"""Read-only presentation statistics tied to a completed, immutable TNK Run."""
from fastapi import APIRouter, Query, Request
from pydantic import BaseModel
from app.core.api_contract import error_response
from app.services.analysis.results.tnk_workbench import TnkWorkbenchUnavailableError, build_tnk_workbench_summary

router = APIRouter(prefix="/api/runs", tags=["tnk-workbench"])


class ScoreState(BaseModel):
    label: str
    color: str
    duration_s: float
    percent: float


class ScoreFrame(BaseModel):
    cursor_s: float
    valid_duration_s: float
    states: list[ScoreState]


class WorkbenchSummary(ScoreFrame):
    run_id: str
    recording_id: str
    valid_points: int
    timeline: list[ScoreFrame]


@router.get("/{run_id}/tnk-workbench", response_model=WorkbenchSummary)
def read_summary(run_id: str, request: Request, cursor_s: float = Query(ge=0, allow_inf_nan=False)):
    try:
        run = request.app.state.run_service.get(run_id)
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    try:
        return build_tnk_workbench_summary(run_id, run, cursor_s)
    except TnkWorkbenchUnavailableError as exc:
        return error_response(request, 409, "TNK_WORKBENCH_RUN_UNAVAILABLE", str(exc))

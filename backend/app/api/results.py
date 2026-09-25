"""Read-only analysis result and reproducible export resources."""

from fastapi import APIRouter, Query, Request
from fastapi.responses import Response

from app.core.api_contract import error_response
from app.services.artifacts import ArtifactIntegrityError


router = APIRouter(prefix="/api/runs", tags=["results"])


@router.get("/{run_id}/result")
def get_result(run_id: str, request: Request):
    try:
        return request.app.state.result_service.view(run_id)
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")


@router.get("/{run_id}/structured-preview")
def structured_preview(run_id: str, request: Request, max_cells: int = Query(default=100_000, ge=1, le=1_000_000)):
    try:
        return request.app.state.result_service.structured_preview(run_id, max_cells=max_cells)
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except ArtifactIntegrityError:
        return error_response(request, 409, "ARTIFACT_INTEGRITY_FAILED", "结果文件校验失败")
    except ValueError as exc:
        return error_response(request, 409, "STRUCTURED_PREVIEW_UNAVAILABLE", str(exc))


@router.get("/{run_id}/export")
def export_result(run_id: str, request: Request, validation_id: str | None = Query(default=None)):
    try:
        package = request.app.state.result_service.export(run_id, validation_id)
    except KeyError:
        return error_response(request, 404, "RESULT_OR_VALIDATION_NOT_FOUND", "结果或校验记录不存在")
    except ArtifactIntegrityError:
        return error_response(request, 409, "ARTIFACT_INTEGRITY_FAILED", "结果文件校验失败")
    return Response(package, media_type="application/zip", headers={"Content-Disposition": f'attachment; filename="run-{run_id}-export.zip"'})

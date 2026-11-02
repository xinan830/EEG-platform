from fastapi import APIRouter, Query, Request

from app.core.api_contract import error_response
from app.services.artifacts import ArtifactIntegrityError, WindowIndexUnavailable
from app.services.topomap import TopomapUnavailable


router = APIRouter(prefix="/api/runs", tags=["topomap"])


@router.get("/{run_id}/topomap")
def rbp_topomap(
    run_id: str,
    request: Request,
    cursor_sample: int | None = Query(default=None, ge=0),
    cursor_s: float | None = Query(default=None, ge=0),
):
    if (cursor_sample is None) == (cursor_s is None):
        return error_response(request, 422, "TOPOMAP_CURSOR_INVALID", "地形图必须且只能提供一种游标坐标。")
    try:
        return request.app.state.topomap_service.build_rbp_window(
            run_id, cursor_sample=cursor_sample, cursor_s=cursor_s,
        ).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except TopomapUnavailable as exc:
        status = 422 if exc.code == "TOPOMAP_CURSOR_INVALID" else 409
        message = exc.message if not exc.detail else f"{exc.message}（{exc.detail}）"
        return error_response(request, status, exc.code, message)
    except ArtifactIntegrityError:
        return error_response(request, 409, "ARTIFACT_INTEGRITY_FAILED", "结果文件校验失败")
    except WindowIndexUnavailable as exc:
        return error_response(request, 409, "RESULT_WINDOW_INDEX_UNAVAILABLE", str(exc))
    except ValueError as exc:
        return error_response(request, 409, "TOPOMAP_UNAVAILABLE", str(exc))


dynamic_router = APIRouter(prefix="/api/analysis-windows", tags=["topomap"])


@dynamic_router.get("/{window_id}/topomap")
def dynamic_topomap(
    window_id: str,
    request: Request,
    project_id: str | None = Query(default=None),
):
    try:
        return request.app.state.topomap_service.build_dynamic_window(
            window_id, project_id=project_id,
        ).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except TopomapUnavailable as exc:
        return error_response(request, 409, exc.code, exc.message)
    except ValueError as exc:
        return error_response(request, 409, "TOPOMAP_UNAVAILABLE", str(exc))

"""Traceable analysis run resources."""

from fastapi import APIRouter, Query, Request, status
from fastapi.responses import JSONResponse

from app.core.api_contract import error_response
from app.models.run import RunCreateRequest
from app.services.runs import RunConflictError, RunService


router = APIRouter(prefix="/api/runs", tags=["runs"])


def _service(request: Request) -> RunService:
    return request.app.state.run_service


@router.post("", status_code=status.HTTP_202_ACCEPTED)
def create_run(payload: RunCreateRequest, request: Request):
    try:
        service = _service(request)
        run = service.enqueue(payload) if hasattr(service, "enqueue") else service.create(payload)
        worker = getattr(request.app.state, "run_worker", None)
        if worker is not None:
            worker.wake()
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "RUN_REQUEST_INVALID", str(exc))
    request.app.state.audit_service.record(
        "run.create", str(getattr(request.state, "request_id", "unknown")),
        recording_id=run.recording_id,
        parameters={"run_id": run.run_id, "analysis_type": run.analysis_type, "status": run.status.value},
    )
    return run.model_dump(mode="json")


@router.get("")
def list_runs(request: Request, recording_id: str | None = None, limit: int = Query(100, ge=1, le=1000)):
    return [item.model_dump(mode="json") for item in _service(request).list(recording_id, limit)]


@router.get("/{run_id}")
def get_run(run_id: str, request: Request):
    try:
        return _service(request).get(run_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")


@router.post("/{run_id}/cancel")
def cancel_run(run_id: str, request: Request):
    try:
        return _service(request).cancel(run_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except (RunConflictError, ValueError) as exc:
        return error_response(request, 409, "RUN_NOT_CANCELLABLE", str(exc))


@router.post("/{run_id}/retry", status_code=status.HTTP_202_ACCEPTED)
def retry_run(run_id: str, request: Request):
    try:
        service = _service(request)
        if not hasattr(service, "retry"):
            return error_response(request, 409, "RUN_RETRY_UNAVAILABLE", "当前运行服务不支持队列重试")
        run = service.retry(run_id)
        worker = getattr(request.app.state, "run_worker", None)
        if worker is not None:
            worker.wake()
        return run.model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")
    except ValueError as exc:
        return error_response(request, 409, "RUN_NOT_RETRYABLE", str(exc))


@router.get("/{run_id}/artifacts")
def list_run_artifacts(run_id: str, request: Request):
    try:
        return [item.model_dump(mode="json") for item in _service(request).list_artifacts(run_id)]
    except KeyError:
        return error_response(request, 404, "RUN_NOT_FOUND", "分析运行不存在")

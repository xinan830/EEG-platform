"""Batch analysis resources for a local research project."""

from fastapi import APIRouter, Request, status

from app.core.api_contract import error_response
from app.models.batch_run import BatchRunCreateRequest
from app.services.batch_runs import BatchProjectMembershipError


router = APIRouter(prefix="/api/batch-runs", tags=["batch-runs"])


@router.post("", status_code=status.HTTP_202_ACCEPTED)
def create_batch(payload: BatchRunCreateRequest, request: Request):
    try:
        batch = request.app.state.batch_run_service.create(payload)
        request.app.state.run_worker.wake()
        return batch.model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "PROJECT_NOT_FOUND", "项目不存在")
    except BatchProjectMembershipError as exc:
        return error_response(request, 422, "BATCH_RECORDING_NOT_IN_PROJECT", str(exc))


@router.get("/{batch_run_id}")
def get_batch(batch_run_id: str, request: Request):
    try:
        return request.app.state.batch_run_service.get(batch_run_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "BATCH_RUN_NOT_FOUND", "批量运行不存在")


@router.get("/{batch_run_id}/items")
def list_items(batch_run_id: str, request: Request):
    try:
        return [item.model_dump(mode="json") for item in request.app.state.batch_run_service.items(batch_run_id)]
    except KeyError:
        return error_response(request, 404, "BATCH_RUN_NOT_FOUND", "批量运行不存在")


@router.post("/{batch_run_id}/cancel")
def cancel_batch(batch_run_id: str, request: Request):
    try:
        return request.app.state.batch_run_service.cancel(batch_run_id).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "BATCH_RUN_NOT_FOUND", "批量运行不存在")


@router.post("/{batch_run_id}/retry", status_code=status.HTTP_202_ACCEPTED)
def retry_batch(batch_run_id: str, request: Request):
    try:
        batch = request.app.state.batch_run_service.retry(batch_run_id)
        request.app.state.run_worker.wake()
        return batch.model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "BATCH_RUN_NOT_FOUND", "批量运行不存在")

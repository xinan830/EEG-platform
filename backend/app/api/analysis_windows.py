"""HTTP resources for opt-in, single dynamic-analysis windows."""

from fastapi import APIRouter, Request, status
from fastapi.responses import JSONResponse
from uuid import uuid4

from app.algorithm_runtime.on_demand import DynamicSubscriptionRequest, DynamicWindowRequest
from app.algorithm_runtime.errors import AlgorithmRuntimeError
from app.core.api_contract import error_response


router = APIRouter(prefix="/api/analysis-windows", tags=["analysis-windows"])


def _service(request: Request):
    return request.app.state.dynamic_window_service


@router.post("/ensure", status_code=status.HTTP_202_ACCEPTED)
def ensure_window(payload: DynamicWindowRequest, request: Request):
    payload = payload.model_copy(update={"request_id": payload.request_id or str(getattr(request.state, "request_id", None) or uuid4().hex)})
    try:
        value = _service(request).ensure(payload)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except AlgorithmRuntimeError as exc:
        http_status = {"WINDOW_CAPACITY_EXCEEDED": 429, "WINDOW_PROJECT_FORBIDDEN": 403,
                       "WINDOW_CURSOR_INVALID": 422}.get(exc.code, 409)
        return error_response(request, http_status, exc.code, exc.message)
    except ValueError as exc:
        message = str(exc)
        return error_response(request, 422, "WINDOW_REQUEST_INVALID", message)
    request.app.state.audit_service.record("dynamic_window.ensure", payload.request_id,
        recording_id=payload.recording_id, session_id=value["session_id"], parameters={
            "window_id": value["window_id"], "subscription_id": value["subscription_id"],
            "algorithm_id": payload.algorithm_id, "project_id": payload.project_id,
            "configuration_sha256": value["identity"]["configuration_sha256"],
            "state": value["state"], "cursor_sample": payload.cursor_sample})
    return JSONResponse(value, status_code=status.HTTP_200_OK if value.get("state") == "ready" else status.HTTP_202_ACCEPTED)


@router.get("/{window_id}")
def get_window(window_id: str, request: Request):
    try:
        return _service(request).get(window_id, project_id=request.query_params.get("project_id"))
    except KeyError:
        return error_response(request, 404, "WINDOW_NOT_FOUND", "分析窗口不存在")
    except ValueError as exc:
        return error_response(request, 409, "WINDOW_RESULT_CORRUPT", str(exc))


@router.post("/{window_id}/subscriptions", status_code=status.HTTP_201_CREATED)
def subscribe(window_id: str, payload: DynamicSubscriptionRequest, request: Request):
    try:
        request_id = str(payload.request_id or getattr(request.state, "request_id", ""))
        return _service(request).subscribe(window_id, request_id, project_id=payload.project_id)
    except (KeyError, ValueError) as exc:
        return error_response(request, 404 if isinstance(exc, KeyError) else 422, "WINDOW_NOT_FOUND" if isinstance(exc, KeyError) else "WINDOW_SUBSCRIPTION_INVALID", str(exc))


@router.delete("/subscriptions/{subscription_id}")
def cancel_subscription(subscription_id: str, request: Request):
    try:
        value = _service(request).cancel_subscription(subscription_id, project_id=request.query_params.get("project_id"))
    except KeyError:
        return error_response(request, 404, "WINDOW_SUBSCRIPTION_NOT_FOUND", "窗口订阅不存在")
    request.app.state.audit_service.record("dynamic_window.cancel_subscription",
        str(getattr(request.state, "request_id", "unknown")), parameters={
            "window_id": value["window_id"], "subscription_id": subscription_id, "state": value["state"]})
    return value

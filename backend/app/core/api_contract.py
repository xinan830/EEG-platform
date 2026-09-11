"""统一 API 错误、请求追踪和基础日志契约。"""

from __future__ import annotations

import logging
import time
from uuid import uuid4

from fastapi import Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from starlette.exceptions import HTTPException as StarletteHTTPException
from starlette.middleware.base import BaseHTTPMiddleware


logger = logging.getLogger("brain_platform.api")


class ApiError(BaseModel):
    """所有 HTTP 错误的稳定结构，detail 仅为旧客户端兼容字段。"""

    code: str
    message: str
    request_id: str
    detail: str


def _request_id(request: Request) -> str:
    return str(getattr(request.state, "request_id", "unknown"))


def error_response(request: Request, status_code: int, code: str, message: str) -> JSONResponse:
    payload = ApiError(
        code=code,
        message=message,
        request_id=_request_id(request),
        detail=message,
    ).model_dump()
    return JSONResponse(status_code=status_code, content=payload)


def _status_code_name(status_code: int) -> str:
    return {
        400: "BAD_REQUEST",
        404: "RESOURCE_NOT_FOUND",
        409: "CONFLICT",
        422: "INVALID_REQUEST",
    }.get(status_code, "HTTP_ERROR")


async def http_exception_handler(request: Request, exc: StarletteHTTPException) -> JSONResponse:
    detail = str(exc.detail)
    return error_response(request, exc.status_code, _status_code_name(exc.status_code), detail)


async def validation_exception_handler(request: Request, exc: RequestValidationError) -> JSONResponse:
    return error_response(request, 422, "INVALID_REQUEST", "请求参数校验失败")


async def unhandled_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.exception("未处理异常 request_id=%s path=%s", _request_id(request), request.url.path)
    return error_response(request, 500, "INTERNAL_ERROR", "服务器内部错误")


class RequestContextMiddleware(BaseHTTPMiddleware):
    """为每个请求生成或继承 request_id，并记录请求耗时。"""

    async def dispatch(self, request: Request, call_next):
        request_id = request.headers.get("X-Request-ID") or uuid4().hex
        request.state.request_id = request_id
        started = time.perf_counter()
        try:
            response = await call_next(request)
        except Exception:
            logger.exception("请求异常 request_id=%s method=%s path=%s", request_id, request.method, request.url.path)
            raise
        elapsed_ms = (time.perf_counter() - started) * 1000
        response.headers["X-Request-ID"] = request_id
        logger.info(
            "请求完成 request_id=%s method=%s path=%s status=%s elapsed_ms=%.1f",
            request_id,
            request.method,
            request.url.path,
            response.status_code,
            elapsed_ms,
        )
        return response

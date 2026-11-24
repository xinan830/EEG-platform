"""Exclude maintenance from complete HTTP responses without serializing normal requests."""

from threading import Lock
from uuid import uuid4

from starlette.requests import Request
from starlette.types import ASGIApp, Receive, Scope, Send

from app.core.api_contract import error_response


CACHE_CLEAR_PATH = "/api/maintenance/cache/clear"


class CacheMaintenanceGate:
    def __init__(self) -> None:
        self._lock = Lock()
        self._readers = 0
        self._exclusive = False

    def try_enter(self, *, exclusive: bool) -> bool:
        with self._lock:
            if self._exclusive or (exclusive and self._readers):
                return False
            if exclusive:
                self._exclusive = True
            else:
                self._readers += 1
            return True

    def leave(self, *, exclusive: bool) -> None:
        with self._lock:
            if exclusive:
                self._exclusive = False
            else:
                self._readers -= 1


class CacheMaintenanceMiddleware:
    def __init__(self, app: ASGIApp, gate: CacheMaintenanceGate) -> None:
        self.app, self.gate = app, gate

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        path = scope.get("path", "").rstrip("/")
        # Live batches own transient buffers, never the persisted caches being cleared.
        if (scope["type"] != "http" or not path.startswith("/api/") or path == "/api/health"
                or path.startswith(("/api/live-filters/", "/api/live-recordings/"))):
            await self.app(scope, receive, send)
            return
        exclusive = path == CACHE_CLEAR_PATH and scope["method"] == "POST"
        if not self.gate.try_enter(exclusive=exclusive):
            request = Request(scope)
            if not getattr(request.state, "request_id", None):
                request.state.request_id = request.headers.get("X-Request-ID") or uuid4().hex
            response = error_response(request, 409, "CACHE_BUSY", "后台正在处理请求或清理缓存，请稍后重试。")
            response.headers["X-Request-ID"] = request.state.request_id
            await response(scope, receive, send)
            return
        try:
            # Await the complete response body, including streaming consumers.
            await self.app(scope, receive, send)
        finally:
            self.gate.leave(exclusive=exclusive)

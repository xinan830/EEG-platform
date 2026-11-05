"""Raw signal export transport and disconnect/resource ownership."""
import asyncio
import logging
from threading import Event
from typing import Literal

from fastapi import APIRouter, Request
from pydantic import BaseModel, ConfigDict
from starlette.responses import FileResponse
from starlette.responses import Response

from app.core.api_contract import error_response
from app.services.recording_signal_export import SignalExportError

router = APIRouter(prefix="/api/recordings", tags=["recordings"])
logger = logging.getLogger(__name__)


class SignalExportRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    format: Literal["edf", "bdf"]


class ExportResponse(FileResponse):
    def __init__(self, package):
        super().__init__(package.path, media_type="application/zip", filename="recording-export.zip")
        self.package = package

    async def __call__(self, scope, receive, send):
        try:
            await super().__call__(scope, receive, send)
        finally:
            self.package.close()


@router.post("/{recording_id}/signal-export", response_class=FileResponse)
async def export(recording_id: str, payload: SignalExportRequest, request: Request) -> Response:
    """Run blocking two-pass I/O outside the event loop and scientific coordinator."""
    try:
        recording = request.app.state.recording_service.require_recording(recording_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录音不存在。")
    cancelled = Event()

    async def watch_disconnect():
        while not cancelled.is_set():
            # The typed body is already consumed; a dedicated receive task avoids polling CancelScopes.
            if (await request.receive())["type"] == "http.disconnect":
                cancelled.set()
                return

    worker = asyncio.create_task(asyncio.to_thread(
        request.app.state.recording_signal_exporter.export, recording, payload.format, cancelled,
        str(getattr(request.state, "request_id", "unknown"))))
    watcher = asyncio.create_task(watch_disconnect())
    try:
        package = await asyncio.shield(worker)
        if cancelled.is_set():
            package.close()
            raise InterruptedError("客户端已断开。")
        return ExportResponse(package)
    except asyncio.CancelledError:
        cancelled.set()
        try:
            package = await asyncio.shield(worker)
            package.close()
        except (Exception, asyncio.CancelledError):
            pass
        raise
    except SignalExportError as error:
        return error_response(request, 409 if error.code == "RECORDING_EXPORT_BUSY" else 422, error.code, str(error))
    except FileNotFoundError:
        return error_response(request, 404, "RECORDING_EXPORT_SOURCE_MISSING", "原始录音文件不存在。")
    except InterruptedError:
        return error_response(request, 499, "RECORDING_EXPORT_CANCELLED", "导出已取消。")
    except ValueError as error:
        return error_response(request, 422, getattr(error, "code", "RECORDING_EXPORT_INVALID"), str(error))
    except OSError:
        logger.exception("recording.signal_export_failed recording_id=%s request_id=%s", recording_id,
                         getattr(request.state, "request_id", "unknown"))
        return error_response(request, 500, "RECORDING_EXPORT_IO_FAILED", "导出读写失败，请检查磁盘空间和后端日志。")
    finally:
        watcher.cancel()
        await asyncio.gather(watcher, return_exceptions=True)

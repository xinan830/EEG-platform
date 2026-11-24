"""FastAPI upload transport for the bounded recording import service."""

import asyncio
from collections.abc import AsyncIterator, Callable
from threading import Event

from fastapi import Request, UploadFile

from app.services.recording_import import EdfBdfImportService, RecordingImportError, StagedRecordingImport


async def stage_upload(request: Request, upload: UploadFile, service: EdfBdfImportService) -> StagedRecordingImport:
    async def chunks() -> AsyncIterator[bytes]:
        while chunk := await upload.read(1024 * 1024):
            yield chunk

    async def check_cancelled() -> None:
        if await request.is_disconnected():
            raise RecordingImportError("RECORDING_IMPORT_CANCELLED", "上传已取消。", 499)

    return await service.stage_upload(upload.filename or "", chunks(), check_cancelled)


async def publish_upload(request: Request, publish: Callable[[Callable[[], None]], object]):
    cancelled = Event()

    def check_cancelled() -> None:
        if cancelled.is_set():
            raise RecordingImportError("RECORDING_IMPORT_CANCELLED", "上传已取消。", 499)

    async def watch_disconnect() -> None:
        while not cancelled.is_set():
            if await request.is_disconnected():
                cancelled.set()
                return
            await asyncio.sleep(0.1)

    worker = asyncio.create_task(asyncio.to_thread(publish, check_cancelled))
    watcher = asyncio.create_task(watch_disconnect())
    try:
        return await asyncio.shield(worker)
    except asyncio.CancelledError:
        cancelled.set()
        try:
            await worker
        except Exception:
            pass
        raise
    finally:
        watcher.cancel()

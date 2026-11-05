"""Transport admission, typed errors, bounded reads and response cleanup."""
from io import BytesIO
import json
from types import SimpleNamespace
from zipfile import ZipFile

from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest

from app.api.recording_signal_export import router
from app.core.api_contract import RequestContextMiddleware
from app.services.recording_signal_export import RecordingSignalExporter
from app.services.recording_samples import WpfSampleReader
from test_recording_signal_export import source_fixture


def client_fixture(tmp_path):
    recording, _ = source_fixture(tmp_path / "source")
    app = FastAPI()
    app.add_middleware(RequestContextMiddleware)
    app.include_router(router)
    def require(recording_id):
        if recording_id != recording.id:
            raise KeyError(recording_id)
        return recording
    app.state.recording_service = SimpleNamespace(require_recording=require)
    app.state.recording_signal_exporter = RecordingSignalExporter(tmp_path / "exports")
    return TestClient(app), app.state.recording_signal_exporter


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_download_package_cleanup_and_errors(tmp_path, format):
    client, exporter = client_fixture(tmp_path)
    response = client.post("/api/recordings/raw-fixture/signal-export", json={"format": format})
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/zip"
    with ZipFile(BytesIO(response.content)) as package:
        assert sorted(package.namelist()) == [f"recording.{format}", f"recording.{format}.labels.json"]
        assert json.loads(package.read(f"recording.{format}.labels.json"))["sample_count"] == 301
    assert not list(exporter.output_root.iterdir())
    assert client.post("/api/recordings/raw-fixture/signal-export", json={"format": "csv"}).status_code == 422
    assert client.post("/api/recordings/raw-fixture/signal-export", json={"format": format, "path": "C:/bad"}).status_code == 422
    assert client.post("/api/recordings/missing/signal-export", json={"format": format}).status_code == 404
    assert exporter.gate.acquire(False)
    try:
        response = client.post("/api/recordings/raw-fixture/signal-export", json={"format": format})
        assert response.status_code == 409
        assert response.json()["code"] == "RECORDING_EXPORT_BUSY"
        assert response.json()["request_id"]
    finally:
        exporter.gate.release()


def test_writer_failure_and_mid_read_cancellation_release_gate(tmp_path, monkeypatch):
    from threading import Event
    recording, _ = source_fixture(tmp_path / "source")
    exporter = RecordingSignalExporter(tmp_path / "exports")
    cancelled = Event()
    original = WpfSampleReader.read
    def read(self, start, stop, channels=None, check_cancelled=None):
        assert stop - start <= 65536
        values = original(self, start, stop, channels, check_cancelled)
        cancelled.set()
        return values
    monkeypatch.setattr(WpfSampleReader, "read", read)
    with pytest.raises(InterruptedError):
        exporter.export(recording, "edf", cancelled)
    assert not list(exporter.output_root.iterdir())
    assert exporter.gate.acquire(False)
    exporter.gate.release()
    monkeypatch.setattr(WpfSampleReader, "read", original)
    def fail(*args):
        raise OSError("disk full")
    monkeypatch.setattr(exporter, "_write", fail)
    with pytest.raises(OSError):
        exporter.export(recording, "bdf", Event())
    assert not list(exporter.output_root.iterdir())
    assert exporter.gate.acquire(False)
    exporter.gate.release()


def test_disconnect_cancels_generation_and_delivery_failure_cleans_package(tmp_path):
    import asyncio
    from threading import Event
    from app.api.recording_signal_export import export, ExportResponse, SignalExportRequest
    from app.services.recording_signal_export import SignalExportPackage
    from threading import Lock

    entered = Event()
    observed = Event()
    def generate(recording, format, cancelled, request_id):
        entered.set()
        assert cancelled.wait(3), "Disconnect did not reach the worker"
        observed.set()
        raise InterruptedError()
    async def receive():
        assert await asyncio.to_thread(entered.wait, 3)
        return {"type": "http.disconnect"}
    request = SimpleNamespace(
        app=SimpleNamespace(state=SimpleNamespace(
            recording_service=SimpleNamespace(require_recording=lambda _: object()),
            recording_signal_exporter=SimpleNamespace(export=generate))),
        state=SimpleNamespace(request_id="disconnect-test"), receive=receive)
    result = asyncio.run(export("raw-id", SignalExportRequest(format="bdf"), request))
    assert result.status_code == 499
    assert observed.is_set()

    directory = tmp_path / "delivery"
    directory.mkdir()
    gate = Lock()
    gate.acquire()
    package = SignalExportPackage(directory, gate)
    package.path.write_bytes(b"package")
    async def send(message):
        if message["type"] == "http.response.body":
            raise OSError("Disconnected during download")
    with pytest.raises(OSError):
        asyncio.run(ExportResponse(package)({"type": "http", "method": "GET", "headers": []}, receive, send))
    assert not directory.exists()
    assert gate.acquire(False)
    gate.release()

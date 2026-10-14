"""Non-identifying WPF source fixture using the real reader and API stack."""

import hashlib
import json
import struct
from pathlib import Path

import numpy as np
from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.testclient import TestClient

from app.api.analysis_windows import router as windows_router
from app.api.results import router as results_router
from app.api.runs import router as runs_router
from app.bootstrap import build_builtin_registry
from app.core.api_contract import RequestContextMiddleware, validation_exception_handler
from app.services.audit import AuditService
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recordings import RecordingService
from app.services.results import ResultService
from app.services.run_queue import PersistentRunQueue
from app.services.validations import ValidationService


def write_recording(root: Path, *, first_counter: int = 700, gap: bool = False,
                    rejected: bool = False, duration_s: float = 22.35,
                    labels: list[str] | None = None) -> None:
    root.mkdir()
    labels = labels or ["F3", "F7"]
    snapshot = {"DeviceSignature": "synthetic-layout",
                "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
                "Channels": [{"NativeChannelIndex": i, "ElectrodeLabel": name, "ExpectedKind": 1}
                             for i, name in enumerate(labels)]}
    montage = {"Id": "synthetic-montage", "DeviceSignature": "synthetic-layout",
               "ChannelConfigurationSnapshot": snapshot, "AverageReferenceLabels": labels,
               "DerivedChannels": [{"Name": f"{name}-REF", "PositiveLabel": name, "NegativeKind": 0,
                                    "NegativeLabels": [], "DisplayOrder": i} for i, name in enumerate(labels)]}
    sfreq = 200
    count = int(round(duration_s * sfreq))
    t = np.arange(count) / sfreq
    signals = np.column_stack([20e-6 * np.sin(2 * np.pi * 10 * t) + 2e-6 * np.sin(2 * np.pi * 6 * t),
                               5e-6 * np.sin(2 * np.pi * 6 * t), first_counter + np.arange(count)])
    if rejected:
        signals[:, 0] *= 30
    ranges = [(0, 1800), (2000, count)] if gap else [(0, count)]
    manifest = {
        "SessionId": "11111111-1111-1111-1111-111111111111",
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V", "DeviceId": "synthetic", "DeviceName": "synthetic",
        "SamplingRateHz": sfreq, "SampleCounterChannelIndex": 2,
        "RecordingStartUtc": "2026-10-01T00:00:00+00:00",
        "HardwareConfiguration": {"montage_configuration_snapshot_json": json.dumps(montage),
                                  "channel_configuration_snapshot_json": json.dumps(snapshot)},
        "Channels": [{"StreamIndex": i, "NativeChannelIndex": i, "Label": name,
                      "Kind": "Reference", "Unit": "V"} for i, name in enumerate(labels)] +
                    [{"StreamIndex": 2, "NativeChannelIndex": 2, "Label": "Counter", "Kind": "SampleCounter", "Unit": "count"}],
    }
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    with (root / "samples-000001.bin").open("wb") as stream:
        for start, end in ranges:
            stream.write(struct.pack("<qiiq", first_counter + start, end - start, 3, 0))
            signals[start:end].astype("<f8").tofile(stream)
    (root / "recording-summary.json").write_text(json.dumps({
        "status": "completed", "effective_sample_count": sum(end - start for start, end in ranges), "sampling_rate_hz": sfreq}), encoding="utf-8")
    (root / "audit.jsonl").write_text('{"type":"completed"}\n', encoding="utf-8")


def source_hashes(root: Path) -> dict[str, str]:
    return {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in root.iterdir() if path.is_file()}


def configure_api(tmp_path: Path, *, gap: bool = False, rejected: bool = False,
                  duration_s: float = 22.35, labels: list[str] | None = None):
    root = tmp_path / "recording"
    write_recording(root, gap=gap, rejected=rejected, duration_s=duration_s, labels=labels)
    recordings = RecordingService(tmp_path / "stored", tmp_path / "catalog.sqlite3")
    recording = recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = PersistentRunQueue(recordings, recordings.database_path, tmp_path / "artifacts")
    windows = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path, tmp_path / "artifacts")
    app = FastAPI()
    app.add_middleware(RequestContextMiddleware)
    app.add_exception_handler(RequestValidationError, validation_exception_handler)
    app.state.dynamic_window_service = windows
    app.state.run_service = runs
    app.state.audit_service = AuditService(recordings.database_path)
    app.state.result_service = ResultService(runs, ValidationService(recordings.database_path))
    app.include_router(windows_router)
    app.include_router(runs_router)
    app.include_router(results_router)
    return TestClient(app), recording, windows, runs, root


def psd_config(recording, *, channel: str = "F3-REF") -> dict:
    return {"algorithm_id": "psd", "channel": channel, "mode": "dynamic",
            "time": {"start_s": 0, "end_s": recording.duration_s},
            "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30, "notch_hz": None}

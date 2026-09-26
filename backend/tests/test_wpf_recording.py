import json
import struct
from pathlib import Path

import numpy as np
from fastapi.testclient import TestClient

from app.main import app
from app.services.recordings import RecordingService
from app.services.wpf_recording import inspect_wpf_recording, load_wpf_data


def _write_fixture(root: Path, status: str = "completed") -> None:
    root.mkdir()
    (root / "manifest.json").write_text(json.dumps({
        "SessionId": "11111111-1111-1111-1111-111111111111",
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V", "DeviceId": "device", "DeviceName": "fixture",
        "SamplingRateHz": 10, "SampleCounterChannelIndex": 1,
        "RecordingStartUtc": "2026-09-26T00:00:00+00:00",
        "Channels": [
            {"StreamIndex": 0, "NativeChannelIndex": 0, "Label": "Fz", "Kind": "Reference", "Unit": "V"},
            {"StreamIndex": 1, "NativeChannelIndex": 1, "Label": "Counter", "Kind": "SampleCounter", "Unit": "count"},
        ],
    }))
    with (root / "samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 100, 4, 2, 0))
        np.asarray([[1e-6, 100], [2e-6, 101], [3e-6, 102], [4e-6, 103]], dtype="<f8").tofile(stream)
    (root / "recording-summary.json").write_text(json.dumps({"status": status, "effective_sample_count": 4, "sampling_rate_hz": 10}))
    (root / "audit.jsonl").write_text('{"type":"completed"}\n')


def test_wpf_fixture_is_loaded_as_v_float64(tmp_path: Path):
    root = tmp_path / "recording"
    _write_fixture(root)
    facts = inspect_wpf_recording(root)
    values, sfreq, names, events = load_wpf_data(facts)
    assert sfreq == 10
    assert names == ["Fz"]
    assert values[:, 0].tolist() == [1e-6, 2e-6, 3e-6, 4e-6]
    assert events == []


def test_register_wpf_is_idempotent_and_rejects_incomplete(tmp_path: Path, monkeypatch):
    root = tmp_path / "recording"
    _write_fixture(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    first = service.register_wpf_recording(str(root))
    second = service.register_wpf_recording(str(root))
    assert first.id == second.id
    assert first.extension == ".wpf"

    incomplete = tmp_path / "incomplete"
    _write_fixture(incomplete, "aborted")
    try:
        service.register_wpf_recording(str(incomplete))
    except ValueError as exc:
        assert getattr(exc, "code") == "WPF_RECORDING_NOT_COMPLETE"
    else:
        raise AssertionError("incomplete recording was accepted")

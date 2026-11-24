import json
import struct
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.services.recording_samples import WpfRecordingFactsCache
from app.services.recordings import RecordingService
from app.services.wpf_recording import WpfRecordingError, inspect_wpf_recording


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


def _write_gap_fixture(root: Path) -> None:
    _write_fixture(root)
    with (root / "samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 100, 2, 2, 0))
        np.asarray([[1e-6, 100], [2e-6, 101]], dtype="<f8").tofile(stream)
        stream.write(struct.pack("<qiiq", 104, 2, 2, 0))
        np.asarray([[3e-6, 104], [np.nan, 105]], dtype="<f8").tofile(stream)
    (root / "audit.jsonl").write_text(
        '{"type":"sample_gap","FirstMissingSampleCounter":102,"LastMissingSampleCounter":103,"MissingSampleCount":2}\n'
        '{"type":"completed"}\n'
    )


def test_wpf_fixture_is_loaded_as_v_float64(tmp_path: Path):
    root = tmp_path / "recording"
    _write_gap_fixture(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root))
    with service.open_access_session(recording) as session:
        first = session.read_samples(0, 1, ["Fz"])
        block = session.read_samples(1, 2, ["Fz"])
        full = session.read_samples(0, 6, ["Fz"])
    assert (first.start_sample, first.end_sample) == (0, 1)
    assert first.values_v[:, 0].tolist() == [1e-6]
    assert block.sampling_rate_hz == 10
    assert block.channel_names == ("Fz",)
    assert block.values_v[:, 0].tolist() == [2e-6]
    np.testing.assert_allclose(full.values_v[:, 0], [1e-6, 2e-6, np.nan, np.nan, 3e-6, np.nan], equal_nan=True)
    assert [(item.start_sample, item.end_sample, item.reason, item.channels) for item in full.unavailable_intervals] == [
        (2, 4, "source_gap", ("Fz",)),
        (5, 6, "missing_value", ("Fz",)),
    ]


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


def test_wpf_facts_cache_reuses_index_and_revalidates_changed_source(tmp_path, monkeypatch):
    from app.services import recording_samples

    root = tmp_path / "recording"
    _write_fixture(root)
    expected_sha256 = inspect_wpf_recording(root).source_sha256
    cache = WpfRecordingFactsCache()
    calls = 0
    inspect = recording_samples.inspect_wpf_recording

    def counted_inspect(directory):
        nonlocal calls
        calls += 1
        return inspect(directory)

    monkeypatch.setattr(recording_samples, "inspect_wpf_recording", counted_inspect)
    first = cache.get(root, expected_sha256)
    second = cache.get(root, expected_sha256)
    assert first is second
    assert calls == 1

    with (root / "audit.jsonl").open("a", encoding="utf-8") as stream:
        stream.write("changed\n")
    with pytest.raises(WpfRecordingError) as exc_info:
        cache.get(root, expected_sha256)
    assert exc_info.value.code == "WPF_RECORDING_SOURCE_IDENTITY_MISMATCH"
    assert calls == 2

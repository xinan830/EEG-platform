import hashlib
import json
import struct
from pathlib import Path
from threading import Event
from types import SimpleNamespace

import mne
import numpy as np
import pytest
import pyedflib

from app.services.recording_signal_export import RecordingSignalExporter, SignalExportError
from app.services.wpf_recording import inspect_wpf_recording


def source_fixture(root: Path, *, gap=False, nonfinite=False, count=301, rate=1000):
    root.mkdir()
    values = np.column_stack((np.linspace(-5e-6, 5e-6, count),
                              np.arange(count) + 100, np.full(count, .999999998)))
    if nonfinite:
        values[-1, 0] = np.nan
    manifest = {
        "SessionId": "11111111-1111-1111-1111-111111111111",
        "RecordingStartUtc": "2026-10-01T16:51:05.564327+00:00",
        "SamplingRateHz": rate, "EegSignalUnit": "V",
        "Channels": [
            {"StreamIndex": 0, "NativeChannelIndex": 4, "Label": "CH0002", "Kind": 1, "Unit": "V"},
            {"StreamIndex": 1, "NativeChannelIndex": 1, "Label": None, "Kind": 4, "Unit": "count"},
            {"StreamIndex": 2, "NativeChannelIndex": 6, "Label": None, "Kind": 2, "Unit": "V"},
        ],
        "HardwareConfiguration": {"hardware_reference_electrode_location": "REF", "serial_number": "private"},
    }
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    (root / "recording-summary.json").write_text('{"status":"completed"}', encoding="utf-8")
    split = count // 2
    with (root / "samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 100, split, 3, 0))
        stream.write(values[:split].astype("<f8").tobytes())
        stream.write(struct.pack("<qiiq", 100 + split + int(gap), count - split, 3, 0))
        stream.write(values[split:].astype("<f8").tobytes())
    (root / "events.json").write_text(json.dumps({"SchemaVersion": 2, "Items": [
        {"RecordingId": manifest["SessionId"].replace("-", ""), "StartSample": 300,
         "DurationSamples": 9, "DefinitionSnapshot": {"Name": "late event"}, "CoordinateStatus": 0},
    ]}), encoding="utf-8")
    facts = inspect_wpf_recording(root)
    recording = SimpleNamespace(id="raw-fixture", stored_name="wpf:" + str(root), extension=".wpf",
                                source_sha256=facts.source_sha256)
    return recording, values[:, [0, 2]]


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_export_roundtrip_samples_labels_events_and_cleanup(tmp_path, format):
    recording, source = source_fixture(tmp_path / "source")
    before = hashlib.sha256((tmp_path / "source/samples-000001.bin").read_bytes()).hexdigest()
    exporter = RecordingSignalExporter(tmp_path / "exports")
    package = exporter.export(recording, format, Event())
    try:
        signal = package.directory / f"recording.{format}"
        read = mne.io.read_raw_edf if format == "edf" else mne.io.read_raw_bdf
        with read(signal, preload=True, verbose="ERROR") as raw:
            assert raw.n_times == len(source)
            assert raw.info["sfreq"] == 1000
            assert raw.ch_names[0] == "CH0002"
            assert len(set(raw.ch_names)) == 2
            assert raw.annotations.description.tolist() == ["late event"]
            assert raw.annotations.onset[0] == pytest.approx(.300, abs=5.1e-5)
            actual = raw.get_data().T * 1e6
        with pyedflib.EdfReader(str(signal)) as header:
            assert header.starttime_subsecond == 5643270
        metadata = json.loads((package.directory / f"recording.{format}.labels.json").read_text(encoding="utf-8"))
        for i, channel in enumerate(metadata["channels"]):
            assert np.max(np.abs(actual[:, i] - source[:, i] * 1e6)) <= channel["quantization_step_uv"] * 1.01
        assert metadata["channels"][1]["source_label"] is None
        assert metadata["hardware"]["hardware_reference_electrode_location"] == "REF"
        assert "serial_number" not in metadata["hardware"]
        assert metadata["events"][0]["duration_samples"] == 1
        assert before == hashlib.sha256((tmp_path / "source/samples-000001.bin").read_bytes()).hexdigest()
        with pytest.raises(SignalExportError, match="正在") as busy:
            exporter.export(recording, format, Event())
        assert busy.value.code == "RECORDING_EXPORT_BUSY"
    finally:
        package.close()
    assert not package.directory.exists()


@pytest.mark.parametrize("problem", ["gap", "nonfinite", "cancel", "derived", "units", "changed"])
def test_export_rejects_bad_source_and_releases_resources(tmp_path, problem):
    recording, _ = source_fixture(tmp_path / "source", gap=problem == "gap", nonfinite=problem == "nonfinite")
    cancelled = Event()
    if problem == "cancel":
        cancelled.set()
    if problem == "derived":
        recording.stored_name = recording.stored_name.replace("wpf:", "wpf-montage:")
    if problem in {"units", "changed"}:
        path = tmp_path / "source/manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["Channels"][0]["Unit"] = "mV"
        path.write_text(json.dumps(manifest))
        if problem == "units":
            recording.source_sha256 = inspect_wpf_recording(path.parent).source_sha256
    exporter = RecordingSignalExporter(tmp_path / "exports")
    with pytest.raises((SignalExportError, ValueError, InterruptedError)):
        exporter.export(recording, "edf", cancelled)
    assert not list((tmp_path / "exports").glob("*"))
    assert exporter.gate.acquire(blocking=False)
    exporter.gate.release()


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_large_dc_offsets_use_exact_outward_encoded_bounds(tmp_path, format):
    recording, _ = source_fixture(tmp_path / "source")
    path = tmp_path / "source/samples-000001.bin"
    values = np.linspace(-28628.27, -27258.21, 301) / 1e6
    with path.open("r+b") as stream:
        for start, count, offset in ((0, 150, 24), (150, 151, 24 + 150 * 24 + 24)):
            for sample in range(count):
                stream.seek(offset + sample * 24)
                stream.write(struct.pack("<d", values[start + sample]))
    recording.source_sha256 = inspect_wpf_recording(path.parent).source_sha256
    package = RecordingSignalExporter(tmp_path / "exports").export(recording, format, Event())
    try:
        read = mne.io.read_raw_edf if format == "edf" else mne.io.read_raw_bdf
        with read(package.directory / f"recording.{format}", preload=True, verbose="ERROR") as raw:
            actual = raw.get_data()[0] * 1e6
        metadata = json.loads((package.directory / f"recording.{format}.labels.json").read_text(encoding="utf-8"))
        channel = metadata["channels"][0]
        assert channel["physical_minimum_uv"] == -28629
        assert channel["physical_maximum_uv"] == -27258
        assert max(abs(actual - values * 1e6)) <= channel["quantization_step_uv"] * 1.01
    finally:
        package.close()


def test_whole_recording_reads_stay_bounded(tmp_path, monkeypatch):
    from app.services.recording_samples import WpfSampleReader
    recording, _ = source_fixture(tmp_path / "source", count=131080)
    original = WpfSampleReader.read
    spans = []
    def read(self, start, stop, channels=None, check_cancelled=None):
        spans.append(stop - start)
        assert stop - start <= 65536
        return original(self, start, stop, channels, check_cancelled)
    monkeypatch.setattr(WpfSampleReader, "read", read)
    package = RecordingSignalExporter(tmp_path / "exports").export(recording, "bdf", Event())
    package.close()
    assert len(spans) > 3


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_short_duration_does_not_underflow_to_previous_tick(tmp_path, format):
    from fractions import Fraction
    recording, _ = source_fixture(tmp_path / "source", count=9)
    package = RecordingSignalExporter(tmp_path / "exports").export(recording, format, Event())
    try:
        signal = package.directory / f"recording.{format}"
        with pyedflib.EdfReader(str(signal)) as header:
            assert header.getNSamples().tolist() == [9, 9]
            assert Fraction(9, header.datarecords_in_file) / Fraction(str(header.datarecord_duration)) == 1000
    finally:
        package.close()

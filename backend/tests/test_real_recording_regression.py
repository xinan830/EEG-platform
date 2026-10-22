"""Regression harness integrity and an anonymous synthetic end-to-end source."""

import copy
import gc
import json
from pathlib import Path
import struct

import numpy as np
import pytest

from scripts import real_recording_regression as cli
from scripts.regression_store import compare_snapshots, load_baseline, save_baseline


def synthetic_recording(root):
    root.mkdir()
    labels, fs = ["F3", "F4", "Fz", "Pz", "Cz"], 200
    channels = dict(Id="fixture-channel", DeviceSignature="fixture-layout",
                    HardwareReferenceElectrodeLocation="REF", HardwareGroundElectrodeLocation="GND",
                    Channels=[dict(NativeChannelIndex=i, ElectrodeLabel=name, ExpectedKind=1)
                              for i, name in enumerate(labels)])
    montage = dict(Id="fixture-ref", Name="private display label", Revision=1,
                   DeviceSignature="fixture-layout", ChannelConfigurationSnapshot=channels,
                   AverageReferenceLabels=labels,
                   DerivedChannels=[dict(Name=name + "-REF", PositiveLabel=name, NegativeKind=0,
                                         NegativeLabels=[], DisplayOrder=i) for i, name in enumerate(labels)])
    manifest = dict(SessionId="11111111-1111-1111-1111-111111111111",
                    PayloadFormat="sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
                    EegSignalUnit="V", DeviceId="fixture", DeviceName="fixture", SamplingRateHz=fs,
                    SampleCounterChannelIndex=len(labels), RecordingStartUtc="2026-10-22T00:00:00+00:00",
                    HardwareConfiguration=dict(montage_configuration_snapshot_json=json.dumps(montage),
                                               channel_configuration_snapshot_json=json.dumps(channels)),
                    Channels=[dict(StreamIndex=i, NativeChannelIndex=i, Label=name, Kind="Reference", Unit="V")
                              for i, name in enumerate(labels)] +
                    [dict(StreamIndex=5, NativeChannelIndex=5, Label="Counter", Kind="SampleCounter", Unit="count")])
    time = np.arange(fs * 36) / fs
    rng = np.random.default_rng(431)
    signals = np.column_stack([
        (8 + i) * 1e-6 * np.sin(2 * np.pi * 10 * time + i * .4)
        + 4e-6 * np.sin(2 * np.pi * 6 * time + i)
        + 3e-6 * np.sin(2 * np.pi * 18 * time + i * .7)
        + rng.normal(0, 1e-6, len(time)) for i in range(5)])
    signals[:fs // 2, 2] += 600e-6 * np.sin(2 * np.pi * 9 * time[:fs // 2])
    values = np.column_stack([signals, np.arange(len(time))])
    with (root / "samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 100, len(values), 6, 0))
        values.astype("<f8").tofile(stream)
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    (root / "recording-summary.json").write_text(json.dumps(dict(
        status="completed", effective_sample_count=len(values), sampling_rate_hz=fs)), encoding="utf-8")
    (root / "audit.jsonl").write_text('{"type":"completed"}\n', encoding="utf-8")
    return root


def test_snapshot_roundtrip_integrity_and_no_overwrite(tmp_path):
    path = tmp_path / "baseline"
    snapshot = dict(source=dict(sha256="source"), state="Rejected", value=None)
    arrays = {"REF/psd/dynamic/psd": np.array([[np.nan, 1e-12]])}
    save_baseline(path, snapshot, arrays)
    restored, restored_arrays = load_baseline(path)
    assert compare_snapshots(restored, restored_arrays, snapshot, arrays)["passed"]
    with pytest.raises(FileExistsError):
        save_baseline(path, {}, {})
    with (path / "arrays.npz").open("ab") as stream:
        stream.write(b"tampered")
    with pytest.raises(ValueError, match="checksum mismatch"):
        load_baseline(path)


@pytest.mark.parametrize("mutation", ["value", "state", "missing", "source", "axis", "density", "dtype"])
def test_comparison_detects_scientific_and_source_changes(mutation):
    expected = dict(source="abc", point=dict(state="Rejected", value=None))
    arrays = {"REF/psd/dynamic/psd": np.array([[np.nan, 1e-12]]),
              "REF/psd/dynamic/axis_frequency_hz": np.array([1., 2.])}
    actual, changed = copy.deepcopy(expected), {key: value.copy() for key, value in arrays.items()}
    if mutation == "value":
        actual["point"]["value"] = 0.
    elif mutation == "state":
        actual["point"]["state"] = "Complete"
    elif mutation == "missing":
        del actual["point"]["value"]
    elif mutation == "source":
        actual["source"] = "xyz"
    elif mutation == "axis":
        changed["REF/psd/dynamic/axis_frequency_hz"][1] += 1e-12
    elif mutation == "density":
        changed["REF/psd/dynamic/psd"][0, 1] *= 2
    else:
        changed["REF/psd/dynamic/psd"] = changed["REF/psd/dynamic/psd"].astype(np.float32)
    result = compare_snapshots(actual, changed, expected, arrays)
    assert not result["passed"]
    if mutation in {"axis", "density"}:
        row = next(row for row in result["checks"] if row["failed"])
        assert row["max_abs"] > 0 and row["failed_coordinates"]


def test_failed_independent_check_cannot_publish_baseline(tmp_path, monkeypatch):
    monkeypatch.setattr(cli, "replay", lambda *a, **kw: (
        dict(scenarios={}), {}, dict(passed=False, failed=1)))
    result = cli.execute("create", tmp_path, tmp_path / "baseline", tmp_path / "report.json")
    assert not result["passed"] and not (tmp_path / "baseline").exists()


def test_all_algorithms_two_montages_replay_and_detect_tampering(tmp_path):
    recording = synthetic_recording(tmp_path / "source")
    baseline, report = tmp_path / "baseline", tmp_path / "report.json"
    result = cli.execute("create", recording, baseline, report, progress=lambda _: None)
    assert result["passed"], [row for row in result["independent"]["checks"] if row["failed"]][:10]
    assert result["scenarios"] == 36
    snapshot, arrays = load_baseline(baseline)
    assert "private display label" not in json.dumps(snapshot)
    assert snapshot["scenarios"]["REF/psd/dynamic"]["result"]["structured"]["windows"][0]["state"] == "Rejected"
    # A second disposable database must not cause allocation identity mismatches.
    result = cli.execute("compare", recording, baseline, report, progress=lambda _: None)
    assert result["passed"], [row for row in result["comparison"]["checks"] if row["failed"]][:10]
    point = snapshot["scenarios"]["AVG/faa/dynamic"]["result"]["metric"]["series"][0]
    point["official"]["faa_evidence"]["p_f3"] *= 2
    assert not compare_snapshots(snapshot, arrays, *load_baseline(baseline))["passed"]
    gc.collect()

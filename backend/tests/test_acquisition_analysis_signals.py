import hashlib
import json
import struct
from pathlib import Path

import numpy as np
import pytest

from app.models.run import RunCreateRequest
from app.services.acquisition_analysis_signals import historical_montage_catalogue, read_acquisition_signal_view
from app.services.recordings import RecordingService
from app.services.runs import RunService
from app.services.wpf_recording import WpfRecordingError


def fixture_recording(root: Path, rows=None):
    root.mkdir()
    labels = ["F3", "F7", "Cz"]
    snapshot = {
        "Id": "recorded-channel-configuration",
        "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": [{"NativeChannelIndex": i, "ElectrodeLabel": label, "ExpectedKind": 1}
                     for i, label in enumerate(labels)],
    }
    montage = {
        "Id": "saved-montage", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": snapshot,
        "AverageReferenceLabels": labels,
        "DerivedChannels": rows or [row("F7", order=0), row("F3", order=1)],
    }
    manifest = {
        "SessionId": "11111111-1111-1111-1111-111111111111",
        "PayloadFormat": "sample_major_float64_v1_with_receive_utc_ticks_and_per_channel_units",
        "EegSignalUnit": "V", "DeviceId": "device", "DeviceName": "fixture",
        "SamplingRateHz": 200, "SampleCounterChannelIndex": 3,
        "RecordingStartUtc": "2026-09-26T00:00:00+00:00",
        "HardwareConfiguration": {
            "montage_configuration_snapshot_json": json.dumps(montage),
            "channel_configuration_snapshot_json": json.dumps(snapshot),
        },
        "Channels": [{"StreamIndex": i, "NativeChannelIndex": i, "Label": label,
                      "Kind": "Reference", "Unit": "V"} for i, label in enumerate(labels)]
                    + [{"StreamIndex": 3, "NativeChannelIndex": 3, "Label": "Counter",
                        "Kind": "SampleCounter", "Unit": "count"}],
    }
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    t = np.arange(2400) / 200
    values = np.column_stack([20e-6 * np.sin(2 * np.pi * 10 * t),
                              5e-6 * np.sin(2 * np.pi * 6 * t),
                              3e-6 * np.sin(2 * np.pi * 8 * t), np.arange(2400)])
    with (root / "samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 0, len(values), 4, 0))
        values.astype("<f8").tofile(stream)
    (root / "recording-summary.json").write_text(json.dumps({
        "status": "completed", "effective_sample_count": len(values), "sampling_rate_hz": 200}))
    (root / "audit.jsonl").write_text('{"type":"completed"}\n')
    return values, manifest


def row(positive, kind=0, negatives=(), order=0):
    suffix = {0: "REF", 1: negatives[0] if negatives else "", 2: "AVG", 3: "PAIR"}[kind]
    return {"Name": f"{positive}-{suffix}", "PositiveLabel": positive,
            "NegativeKind": kind, "NegativeLabels": list(negatives), "DisplayOrder": order}


def test_registration_exposes_only_saved_outputs_and_preserves_raw(tmp_path):
    root = tmp_path / "recording"
    values, _ = fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    raw = service.register_wpf_recording(str(root))
    derived = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    assert list(raw.channels) == ["F3", "F7", "Cz"]
    assert list(derived.channels) == ["F7-REF", "F3-REF"]
    assert derived.raw_channel_labels == raw.channels
    assert raw.id != derived.id
    assert raw.source_sha256 == derived.source_sha256
    assert len(derived.montage_catalogue) == 1
    catalogue = derived.montage_catalogue[0]
    assert catalogue["is_acquisition_default"] is True
    assert catalogue["fingerprint"]
    assert [item["id"] for item in catalogue["outputs"]] == ["F7-REF", "F3-REF"]
    assert catalogue["outputs"][0]["role"] == "F7"
    assert service.register_wpf_recording(str(root), signal_mode="acquisition_montage").id == derived.id
    reloaded = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    reader = reloaded.open_sample_reader(reloaded.require_recording(derived.id))
    try:
        np.testing.assert_array_equal(reader.read(3, 25), values[3:25, [1, 0]])
        np.testing.assert_array_equal(reader.read(3, 25, [1]), values[3:25, [0]])
    finally:
        reader.close()
    data, _, names, _ = service.load_data(derived)
    assert names == list(derived.channels)
    np.testing.assert_array_equal(data, values[:, [1, 0]])


def test_record_without_alternate_catalogue_reads_default_but_rejects_switch(tmp_path):
    root = tmp_path / "recording"
    values, _ = fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    assert [profile["id"] for profile in recording.montage_catalogue] == ["saved-montage"]
    reader = service.open_sample_reader(recording, analysis_montage_id="saved-montage")
    try:
        np.testing.assert_array_equal(reader.read(0, 10), values[:10, [1, 0]])
    finally:
        reader.close()
    with pytest.raises(WpfRecordingError, match="requested historical montage is not present"):
        read_acquisition_signal_view(root, "unsaved-average")


def test_output_colors_and_custom_order_do_not_remap_physical_sources(tmp_path):
    root = tmp_path / "recording"
    rows = [dict(row("F3", order=1), Color="#112233"),
            dict(row("F7", order=0), Color="#445566")]
    values, _ = fixture_recording(root, rows)
    original = (root / "samples-000001.bin").read_bytes()
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    derived = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    assert list(derived.channels) == ["F7-REF", "F3-REF"]
    reader = service.open_sample_reader(derived)
    try:
        np.testing.assert_array_equal(reader.read(0, 200), values[:200, [1, 0]])
    finally:
        reader.close()
    assert (root / "samples-000001.bin").read_bytes() == original


@pytest.mark.parametrize("kind,negatives", [(1, ("F7",)), (2, ("F3", "F7", "Cz")), (3, ("F7", "Cz"))])
def test_formula_projects_all_reference_dependencies(tmp_path, kind, negatives):
    root = tmp_path / "recording"
    values, _ = fixture_recording(root, [row("F3", kind, negatives)])
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    derived = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    reader = service.open_sample_reader(derived)
    try:
        indexes = [{"F3": 0, "F7": 1, "Cz": 2}[name] for name in negatives]
        np.testing.assert_allclose(reader.read(0, 200)[:, 0], values[:200, 0] - values[:200, indexes].mean(axis=1), atol=0)
        assert reader.read(0, 200).dtype == np.float64
    finally:
        reader.close()


@pytest.mark.parametrize("damage", ["missing_snapshot", "swapped_mapping", "missing_negative", "unit", "kind", "duplicate", "mean_group", "device", "name"])
def test_unverifiable_snapshot_rejected_without_raw_fallback(tmp_path, damage):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    hardware = manifest["HardwareConfiguration"]
    montage = json.loads(hardware["montage_configuration_snapshot_json"])
    if damage == "missing_snapshot":
        del hardware["channel_configuration_snapshot_json"]
    elif damage == "swapped_mapping":
        montage["ChannelConfigurationSnapshot"]["Channels"][0]["NativeChannelIndex"] = 1
    elif damage == "missing_negative":
        montage["DerivedChannels"] = [row("F3", 1, ("M1",))]
    elif damage in {"unit", "kind"}:
        manifest["Channels"][0]["Unit" if damage == "unit" else "Kind"] = "uV" if damage == "unit" else "SampleCounter"
    elif damage == "duplicate":
        montage["DerivedChannels"] = [row("F3"), row("F3", order=1)]
    elif damage == "mean_group":
        montage["DerivedChannels"] = [row("F3", 2, ("F3", "F7"))]
    elif damage == "device":
        montage["ChannelConfigurationSnapshot"]["DeviceSignature"] = "another-layout"
    elif damage == "name":
        montage["DerivedChannels"][0]["Name"] = "F3-REF"
    hardware["montage_configuration_snapshot_json"] = json.dumps(montage)
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(WpfRecordingError, match="采集导联快照无法验证"):
        read_acquisition_signal_view(root)


def test_nan_source_propagates_to_derived_signal(tmp_path):
    from app.services.acquisition_analysis_signals import AcquisitionMontageSampleReader
    from app.services.recording_samples import ArraySampleReader
    root = tmp_path / "recording"
    fixture_recording(root, [row("F3", 3, ("F7", "Cz"))])
    reader = AcquisitionMontageSampleReader(ArraySampleReader(np.array([[1., np.nan, 2.], [3., 1., 1.]]), 200, ["F3", "F7", "Cz"]), read_acquisition_signal_view(root))
    assert np.isnan(reader.read(0, 2)[0, 0])
    assert reader.read(0, 2)[1, 0] == 2.


@pytest.mark.parametrize("analysis_type", ["spectrum", "spectrogram"])
def test_run_persists_signal_formula_and_accurate_reference(tmp_path, analysis_type):
    root = tmp_path / "recording"
    fixture_recording(root, [row("F3", 1, ("F7",))])
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    run = runs.create(RunCreateRequest(recording_id=recording.id, analysis_type=analysis_type,
        config={"channels": ["F3-F7"], "time": {"start_s": 0, "end_s": 12}}))
    assert run.status == "completed", run.error
    assert run.reference["mode"] == "acquisition_montage_snapshot"
    source = run.channel_mapping["signal_source"]
    assert source["formulas"][0]["negative_labels"] == ["F7"]
    assert source["source_mapping"]["F3"]["native_channel_index"] == 0
    assert run.result_summary["analysis_reference"] == "acquisition_montage_snapshot"


@pytest.mark.parametrize("algorithm_id", ["psd", "stft", "rbp", "theta_beta", "iapf", "peak_frequency"])
@pytest.mark.parametrize("mode", ["static", "dynamic"])
def test_official_algorithms_use_registered_montage_signals(tmp_path, algorithm_id, mode):
    root = tmp_path / "recording"
    fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    run = runs.create(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": algorithm_id, "channel": "F3-REF", "mode": mode,
        "time": {"start_s": 0, "end_s": 12}, "dynamic_window_s": 5, "refresh_step_s": 1,
    }))
    assert run.status in {"completed", "gate_failed"}, run.error
    assert run.channel_mapping["channels"] == ["F3-REF"]
    assert run.reference["mode"] == "acquisition_montage_snapshot"


def test_source_projection_enters_cache_identity(tmp_path):
    root = tmp_path / "recording"
    fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    request = RunCreateRequest(recording_id=recording.id, config={
        "channels": ["F3-REF"], "time": {"start_s": 0, "end_s": 12}})
    resolved = runs._resolve_request(request, recording)
    source = service.analysis_signal_provenance(recording)
    service.analysis_signal_provenance = lambda _: {**source, "snapshot_sha256": "different-snapshot"}
    changed = runs._resolve_request(request, recording)
    assert changed["definition_sha256"] != resolved["definition_sha256"]


def test_official_run_rejects_stale_historical_montage_identity(tmp_path):
    root = tmp_path / "recording"
    fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    profile = recording.montage_catalogue[0]
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3-REF", "mode": "static",
        "time": {"start_s": 0, "end_s": 12}, "analysis_montage_id": profile["id"],
        "analysis_montage_revision": profile["revision"], "analysis_montage_fingerprint": "stale",
        "analysis_montage_outputs": [{"role": "F3", "output": "F3-REF", "unit": "V"}],
    })
    with pytest.raises(ValueError, match="ANALYSIS_MONTAGE_STALE"):
        runs._resolve_request(request, recording)


def test_official_run_rejects_valid_montage_with_wrong_role_mapping(tmp_path):
    root = tmp_path / "recording"
    fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    profile = recording.montage_catalogue[0]
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3-REF", "mode": "static",
        "time": {"start_s": 0, "end_s": 12}, "analysis_montage_id": profile["id"],
        "analysis_montage_revision": profile["revision"], "analysis_montage_fingerprint": profile["fingerprint"],
        "analysis_montage_outputs": [{"role": "wrong-role", "output": "F3-REF", "unit": "V"}],
    })
    with pytest.raises(ValueError, match="ANALYSIS_MONTAGE_MAPPING_MISMATCH"):
        runs._resolve_request(request, recording)


@pytest.mark.parametrize("reference_kind, negatives", [(2, ("F3", "F7", "Cz")), (1, ("F7",))])
def test_historical_catalogue_projects_selected_profile_from_same_raw_channels(tmp_path, reference_kind, negatives):
    root = tmp_path / "recording"
    values, manifest = fixture_recording(root)
    hardware = manifest["HardwareConfiguration"]
    alternative = {
        "Id": "saved-average", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": json.loads(hardware["channel_configuration_snapshot_json"]),
        "AverageReferenceLabels": ["F3", "F7", "Cz"],
        "DerivedChannels": [row("F3", reference_kind, negatives, order=0)],
    }
    hardware["montage_catalogue_snapshot_json"] = json.dumps([
        json.loads(hardware["montage_configuration_snapshot_json"]), alternative,
    ])
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    source_paths = [root / "manifest.json", root / "samples-000001.bin"]
    source_checksums = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths}
    catalogue = historical_montage_catalogue(root)
    assert [item["id"] for item in catalogue] == ["saved-montage", "saved-average"]
    assert [item["is_acquisition_default"] for item in catalogue] == [True, False]
    assert historical_montage_catalogue(root, read_acquisition_signal_view(root)) == catalogue
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    assert recording.montage_catalogue == catalogue
    assert service.register_wpf_recording(str(root), signal_mode="acquisition_montage").montage_catalogue == catalogue
    assert service.require_recording(recording.id).montage_catalogue == catalogue
    restarted = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    assert restarted.require_recording(recording.id).montage_catalogue == catalogue
    reader = service.open_sample_reader(
        recording,
        analysis_montage_id="saved-average",
    )
    try:
        reference_indexes = [0, 1, 2] if reference_kind == 2 else [1]
        np.testing.assert_allclose(reader.read(0, 10)[:, 0], values[:10, 0] - values[:10, reference_indexes].mean(axis=1))
    finally:
        reader.close()
    assert {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths} == source_checksums


def compatible_average_profile(manifest):
    snapshot = json.loads(manifest["HardwareConfiguration"]["channel_configuration_snapshot_json"])
    return {
        "Id": "current-average-profile", "Name": "当前平均参考", "Revision": 3,
        "Fingerprint": "client-fingerprint-is-not-authoritative", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": snapshot,
        "AverageReferenceLabels": ["F3", "F7", "Cz"],
        "DerivedChannels": [row("F3", 2, ("F3", "F7", "Cz"), order=0)],
    }


def test_explicit_compatible_profile_is_persisted_resolved_and_keeps_raw_immutable(tmp_path):
    root = tmp_path / "recording"
    values, manifest = fixture_recording(root)
    profile = compatible_average_profile(manifest)
    raw_paths = [root / "manifest.json", root / "samples-000001.bin"]
    original_checksums = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in raw_paths}
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")

    recording = service.register_wpf_recording(
        str(root), signal_mode="acquisition_montage", analysis_montage_profiles=[profile],
    )
    snapshots = [item for item in recording.montage_catalogue if item.get("snapshot_source") == "analysis_configuration_snapshot"]
    snapshot = snapshots[0]
    assert snapshot["id"].startswith("analysis-montage-")
    assert snapshot["id"] != profile["Id"]
    assert snapshot["source_profile_id"] == profile["Id"]
    assert snapshot["fingerprint"] == snapshot["id"].removeprefix("analysis-montage-")
    assert snapshot["revision"] == "3"

    selected = service.select_analysis_montage(recording, snapshot["id"])
    assert selected.channels == ("F3-AVG",)
    reader = service.open_sample_reader(selected)
    try:
        expected = values[:20, 0] - values[:20, :3].mean(axis=1)
        np.testing.assert_allclose(reader.read(0, 20)[:, 0], expected, rtol=0, atol=1e-18)
    finally:
        reader.close()

    reloaded = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    persisted = reloaded.require_recording(recording.id)
    assert next(item for item in persisted.montage_catalogue if item["id"] == snapshot["id"]) == snapshot
    assert {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in raw_paths} == original_checksums


def test_official_run_uses_explicit_analysis_snapshot_channels_and_provenance(tmp_path):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    profile = compatible_average_profile(manifest)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(
        str(root), signal_mode="acquisition_montage", analysis_montage_profiles=[profile],
    )
    selected = next(item for item in recording.montage_catalogue if item.get("snapshot_source") == "analysis_configuration_snapshot")
    runs = RunService(service, service.database_path, tmp_path / "artifacts")
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3-AVG", "mode": "static",
        "time": {"start_s": 0, "end_s": 12}, "analysis_montage_id": selected["id"],
        "analysis_montage_revision": selected["revision"], "analysis_montage_fingerprint": selected["fingerprint"],
        "analysis_montage_outputs": [{"role": "F3", "output": "F3-AVG", "unit": "V"}],
    })

    run = runs.create(request)
    assert run.status in {"completed", "gate_failed"}, run.error
    assert run.channel_mapping["channels"] == ["F3-AVG"]
    assert run.channel_mapping["analysis_montage"]["id"] == selected["id"]
    source = run.channel_mapping["signal_source"]
    assert source["snapshot_source"] == "analysis_configuration_snapshot"
    assert source["source_profile_id"] == profile["Id"]
    assert source["montage_id"] == selected["id"]


def test_queued_official_run_resolves_selected_montage_before_channel_validation(tmp_path):
    from app.services.run_queue import PersistentRunQueue

    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    profile = compatible_average_profile(manifest)
    recordings = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = recordings.register_wpf_recording(
        str(root), signal_mode="acquisition_montage", analysis_montage_profiles=[profile],
    )
    snapshot = next(
        item for item in recording.montage_catalogue
        if item.get("snapshot_source") == "analysis_configuration_snapshot"
    )
    queue = PersistentRunQueue(recordings, recordings.database_path, tmp_path / "artifacts")
    request = RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": "peak_frequency", "channel": "F3-AVG", "mode": "static",
        "time": {"start_s": 0, "end_s": 12}, "analysis_montage_id": snapshot["id"],
        "analysis_montage_revision": snapshot["revision"],
        "analysis_montage_fingerprint": snapshot["fingerprint"],
        "analysis_montage_outputs": [{"role": "F3", "output": "F3-AVG", "unit": "V"}],
    })

    queued = queue.enqueue(request)
    assert queued.channel_mapping["channels"] == ["F3-AVG"]
    result = queue.process_next()
    assert result is not None
    assert result.status in {"completed", "gate_failed"}, result.error
    assert result.channel_mapping["analysis_montage"]["id"] == snapshot["id"]


def test_first_registration_api_returns_complete_montage_catalogue(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from app.main import app

    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    hardware = manifest["HardwareConfiguration"]
    default = json.loads(hardware["montage_configuration_snapshot_json"])
    alternative = {**default, "Id": "saved-bipolar", "DerivedChannels": [row("F3", 1, ("F7",))]}
    hardware["montage_catalogue_snapshot_json"] = json.dumps([default, alternative])
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state.audit_service, "record", lambda *args, **kwargs: None)
    client = TestClient(app)
    payload = {"source_directory": str(root), "signal_mode": "acquisition_montage"}
    first = client.post("/api/recordings/register-wpf", json=payload)
    assert first.status_code == 201
    catalogue = first.json()["montage_catalogue"]
    assert [profile["id"] for profile in catalogue] == ["saved-montage", "saved-bipolar"]
    assert catalogue[1]["outputs"][0]["id"] == "F3-F7"
    assert client.post("/api/recordings/register-wpf", json=payload).json()["montage_catalogue"] == catalogue
    assert client.get(f"/api/recordings/{first.json()['id']}").json()["montage_catalogue"] == catalogue


def test_brainbeat_hardware_reference_roles_are_not_guessed_from_bipolar_names():
    from app.algorithms.brainbeat.config import BrainbeatConfig
    BrainbeatConfig(channel="Fz-REF", secondary_channel="Pz-REF", mode="static", start_s=0, end_s=10)
    with pytest.raises(ValueError):
        BrainbeatConfig(channel="Fz-Pz", secondary_channel="Pz-REF", mode="static", start_s=0, end_s=10)


def test_ref_spectrum_matches_the_same_historical_raw_signal(tmp_path):
    root = tmp_path / "recording"
    fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    raw = service.register_wpf_recording(str(root))
    derived = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    raw_psd = service.load_spectrum(raw, 0, 12, ["F3"])
    derived_psd = service.load_spectrum(derived, 0, 12, ["F3-REF"])
    assert raw_psd["frequencies_hz"] == derived_psd["frequencies_hz"]
    np.testing.assert_allclose(raw_psd["psd"]["F3"], derived_psd["psd"]["F3-REF"], rtol=1e-12, atol=0)

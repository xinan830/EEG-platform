"""Validate generated test recordings through the real analysis boundary."""

import json
from uuid import UUID

import numpy as np
import pytest

from app.services.acquisition_analysis_signals import read_acquisition_signal_view
from app.services.recordings import RecordingService
from app.services.wpf_recording import WpfRecordingError, inspect_wpf_recording
from tools import generate_synthetic_24h_recording as generator


@pytest.fixture
def synthetic_recording(tmp_path, monkeypatch):
    # Exercise multiple batches without writing a 24-hour fixture in every test.
    monkeypatch.setattr(generator, "DURATION_S", 12)
    monkeypatch.setattr(generator, "SAMPLE_COUNT", 12 * generator.SAMPLE_RATE_HZ)
    monkeypatch.setattr(generator, "CHUNK_SAMPLES", 1000)
    root = tmp_path / "recording"
    generator.write_recording(root)
    return root


def test_generated_manifest_has_explicit_reference_and_desktop_context(synthetic_recording):
    manifest = json.loads((synthetic_recording / "manifest.json").read_text(encoding="utf-8"))
    assert UUID(manifest["SessionId"]).int != 0
    assert all(manifest["Project"][key] for key in ("Id", "Number", "Name", "DirectoryPath", "SnapshotJson"))
    hardware = manifest["HardwareConfiguration"]
    snapshot = json.loads(hardware["channel_configuration_snapshot_json"])
    montage = json.loads(hardware["montage_configuration_snapshot_json"])
    assert montage["ChannelConfigurationSnapshot"] == snapshot
    view = read_acquisition_signal_view(synthetic_recording)
    assert view.provenance["hardware_reference"] == "REF"
    assert view.provenance["hardware_ground"] == "GND"
    assert [signal.name for signal in view.signals] == ["F3-REF", "F7-REF"]


def test_generated_recording_registers_and_projects_exact_raw_signals(synthetic_recording, tmp_path):
    facts = inspect_wpf_recording(synthetic_recording)
    assert facts.sample_count == 3000
    assert facts.duration_s == 12
    service = RecordingService(tmp_path / "stored", tmp_path / "catalog.sqlite3")
    raw = service.register_wpf_recording(str(synthetic_recording))
    derived = service.register_wpf_recording(str(synthetic_recording), signal_mode="acquisition_montage")
    assert derived.channels == ("F3-REF", "F7-REF")
    assert raw.source_sha256 == derived.source_sha256
    with service.open_access_session(raw) as raw_session, service.open_access_session(derived) as derived_session:
        # Include a batch boundary and both ends of the recording.
        for start, stop in [(0, 250), (900, 1100), (2750, 3000)]:
            values = derived_session.read_samples(start, stop).values_v
            assert values.dtype == np.float64
            assert np.isfinite(values).all()
            np.testing.assert_array_equal(values, raw_session.read_samples(start, stop).values_v)


@pytest.mark.parametrize("key", ["HardwareReferenceElectrodeLocation", "HardwareGroundElectrodeLocation"])
@pytest.mark.parametrize("target", ["recorded", "bound"])
def test_missing_reference_metadata_is_not_silently_defaulted(synthetic_recording, key, target):
    path = synthetic_recording / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    hardware = manifest["HardwareConfiguration"]
    if target == "recorded":
        snapshot = json.loads(hardware["channel_configuration_snapshot_json"])
        del snapshot[key]
        hardware["channel_configuration_snapshot_json"] = json.dumps(snapshot)
    else:
        montage = json.loads(hardware["montage_configuration_snapshot_json"])
        del montage["ChannelConfigurationSnapshot"][key]
        hardware["montage_configuration_snapshot_json"] = json.dumps(montage)
    path.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(WpfRecordingError, match="hardware reference/ground mismatch") as error:
        read_acquisition_signal_view(synthetic_recording)
    assert error.value.code == "ACQUISITION_MONTAGE_UNVERIFIABLE"

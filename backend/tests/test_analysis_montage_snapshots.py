"""Explicit current presets become immutable, recording-bound analysis inputs."""

import copy
import json
import time

import numpy as np
import pytest

from app.algorithm_runtime.on_demand import DynamicWindowRequest
from app.bootstrap import build_builtin_registry
from app.services.on_demand_dynamic import DynamicWindowService
from app.services.recordings import RecordingService
from app.services.wpf_recording import WpfRecordingError
from test_acquisition_analysis_signals import fixture_recording, compatible_average_profile, row


@pytest.mark.parametrize("invalid_field", ["device", "configuration_id", "mapping", "reference", "dependency", "revision"])
def test_invalid_candidate_rejects_entire_snapshot_batch(tmp_path, invalid_field):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    valid = compatible_average_profile(manifest)
    invalid = copy.deepcopy(valid)
    invalid["Id"] = "invalid-profile"
    bound = invalid["ChannelConfigurationSnapshot"]
    if invalid_field == "device":
        invalid["DeviceSignature"] = "different-device"
    elif invalid_field == "configuration_id":
        bound["Id"] = "different-configuration"
    elif invalid_field == "mapping":
        bound["Channels"][2]["NativeChannelIndex"] = 999
    elif invalid_field == "reference":
        bound["HardwareReferenceElectrodeLocation"] = "different-reference"
    elif invalid_field == "dependency":
        invalid["DerivedChannels"] = [row("F3", 1, ("missing",))]
    else:
        invalid["Revision"] = 0
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = service.register_wpf_recording(str(root), signal_mode="acquisition_montage")
    with pytest.raises(WpfRecordingError) as error:
        service.register_wpf_recording(str(root), signal_mode="acquisition_montage",
                                       analysis_montage_profiles=[valid, invalid])
    assert error.value.code in {"ANALYSIS_MONTAGE_INCOMPATIBLE", "ANALYSIS_MONTAGE_INVALID"}
    assert service.require_recording(recording.id).montage_catalogue == recording.montage_catalogue
    with service._connect() as connection:
        assert connection.execute("SELECT count(*) FROM analysis_montage_snapshots").fetchone()[0] == 0


def test_invalid_first_registration_does_not_leave_recording_or_snapshot_rows(tmp_path):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    invalid = compatible_average_profile(manifest)
    invalid["DeviceSignature"] = "different-device"
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    with pytest.raises(WpfRecordingError):
        service.register_wpf_recording(str(root), signal_mode="acquisition_montage",
                                       analysis_montage_profiles=[invalid])
    with service._connect() as connection:
        assert connection.execute("SELECT count(*) FROM recordings").fetchone()[0] == 0
        assert connection.execute("SELECT count(*) FROM analysis_montage_snapshots").fetchone()[0] == 0


def test_registration_api_accepts_compatible_profile_and_rejects_incompatible_profile(tmp_path, monkeypatch):
    from fastapi.testclient import TestClient
    from app.main import app

    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    monkeypatch.setattr(app.state, "recording_service", service)
    monkeypatch.setattr(app.state.audit_service, "record", lambda *args, **kwargs: None)
    client = TestClient(app)
    profile = compatible_average_profile(manifest)
    payload = {"source_directory": str(root), "signal_mode": "acquisition_montage",
               "analysis_montage_profiles": [profile]}

    response = client.post("/api/recordings/register-wpf", json=payload)

    assert response.status_code == 201
    entry = next(item for item in response.json()["montage_catalogue"]
                 if item["snapshot_source"] == "analysis_configuration_snapshot")
    assert entry["source_profile_id"] == profile["Id"]
    assert entry["id"].startswith("analysis-montage-")

    invalid_root = tmp_path / "invalid-recording"
    _, invalid_manifest = fixture_recording(invalid_root)
    invalid = compatible_average_profile(invalid_manifest)
    invalid["DeviceSignature"] = "another-device"
    rejected = client.post("/api/recordings/register-wpf", json={
        **payload, "source_directory": str(invalid_root), "analysis_montage_profiles": [invalid],
    })
    assert rejected.status_code == 422
    assert rejected.json()["code"] == "ANALYSIS_MONTAGE_INCOMPATIBLE"
    with service._connect() as connection:
        assert connection.execute("SELECT count(*) FROM recordings").fetchone()[0] == 1


def test_edited_formula_creates_new_identity_without_overwriting_old_snapshot(tmp_path):
    root = tmp_path / "recording"
    values, manifest = fixture_recording(root)
    profile = compatible_average_profile(manifest)
    service = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    def register():
        return service.register_wpf_recording(str(root), signal_mode="acquisition_montage",
                                              analysis_montage_profiles=[profile])
    first = register()
    first_snapshot = first.montage_catalogue[-1]
    assert register().montage_catalogue == first.montage_catalogue
    profile["DerivedChannels"] = [row("F3", 1, ("F7",))]
    changed = register()
    assert len(changed.montage_catalogue) == 3
    new_snapshot = next(item for item in changed.montage_catalogue if item["id"] not in
                        {entry["id"] for entry in first.montage_catalogue})
    assert new_snapshot["fingerprint"] != first_snapshot["fingerprint"]
    assert first_snapshot in changed.montage_catalogue
    for snapshot, expected in [(first_snapshot, values[:10, 0] - values[:10, :3].mean(axis=1)),
                               (new_snapshot, values[:10, 0] - values[:10, 1])]:
        reader = service.open_sample_reader(changed, analysis_montage_id=snapshot["id"])
        try:
            np.testing.assert_allclose(reader.read(0, 10)[:, 0], expected, rtol=0, atol=1e-18)
        finally:
            reader.close()


def test_dynamic_window_projects_selected_snapshot_and_isolates_default_cache(tmp_path):
    root = tmp_path / "recording"
    _, manifest = fixture_recording(root)
    recordings = RecordingService(tmp_path / "stored", tmp_path / "db.sqlite3")
    recording = recordings.register_wpf_recording(str(root), signal_mode="acquisition_montage",
        analysis_montage_profiles=[compatible_average_profile(manifest)])
    service = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path, tmp_path / "artifacts")
    try:
        results = []
        for profile in recording.montage_catalogue:
            output = next(item for item in profile["outputs"] if item["role"] == "F3")
            config = {"channel": output["id"], "mode": "dynamic", "time": {"start_s": 0, "end_s": 12},
                      "dynamic_window_s": 5, "refresh_step_s": 1, "low_hz": 1, "high_hz": 30,
                      "analysis_montage_id": profile["id"], "analysis_montage_revision": profile["revision"],
                      "analysis_montage_fingerprint": profile["fingerprint"],
                      "analysis_montage_outputs": [{"role": "F3", "output": output["id"], "unit": "V"}]}
            response = service.ensure(DynamicWindowRequest(recording_id=recording.id, algorithm_id="psd",
                cursor_sample=2000, request_id=profile["id"], config=config))
            deadline = time.monotonic() + 10
            while response["state"] in {"queued", "preparing_source", "computing_window"} and time.monotonic() < deadline:
                time.sleep(0.02)
                response = service.get(response["window_id"])
            assert response["state"] == "ready", response
            session = service.repository.get_session(response["session_id"])
            signal_mapping = session.configuration["_scientific_context"]["signal_mapping"]
            assert profile["id"] in json.dumps(signal_mapping)
            assert output["id"] in json.dumps(signal_mapping)
            if profile["id"].startswith("analysis-montage-"):
                assert signal_mapping["analysis_montage"]["snapshot_source"] == "analysis_configuration_snapshot"
                assert signal_mapping["analysis_montage"]["source_profile_id"] == "current-average-profile"
            results.append(response)
        assert results[0]["window_id"] != results[1]["window_id"]
        assert results[0]["result"]["arrays"]["psd"] != results[1]["result"]["arrays"]["psd"]
    finally:
        service.close(wait=True)

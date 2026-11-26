"""Physiological identity is independent of artifact scoring selection."""
import json
from dataclasses import replace
from threading import Event

import numpy as np
import pytest

from app.models.artifact_references import ChannelIdentitySelection, ChannelIdentityUpdate
from app.scientific.contracts.preprocessing import ArtifactRemovalSpec
from app.services.artifact_inspection import inspect_recording_ica_components
from app.services.artifact_references import discover_artifact_references
from app.services.recording_channel_roles import save_recording_identities, source_channel_identities
from app.services.recording_signal_export import RecordingSignalExporter
from app.services.recordings import RecordingService
from test_artifact_reference_channels import make_recording


def save(service, recording, **roles):
    return save_recording_identities(service, recording, ChannelIdentityUpdate(
        source_sha256=recording.source_sha256,
        channels=[ChannelIdentitySelection(name=name, identity=role) for name, role in roles.items()]))


@pytest.mark.parametrize("montage", [False, True])
@pytest.mark.parametrize("with_ecg", [False, True])
def test_auto_and_explicit_eeg_proxies_have_identical_ica_inputs_scores_and_output(tmp_path, montage, with_ecg):
    # Rename at acquisition time, before registration/source hashing.
    first, old, _ = make_recording(tmp_path)
    root = tmp_path / "source"
    path = root / "manifest.json"
    manifest = json.loads(path.read_text())
    manifest["Channels"][0]["Label"] = "Fp1"
    manifest["Channels"][1]["Label"] = "Fp2"
    for row in manifest["Channels"][4:6]:
        row["PhysiologicalIdentity"] = "unconfirmed"
    if with_ecg:
        manifest["Channels"][5]["PhysiologicalIdentity"] = "ecg"
    path.write_text(json.dumps(manifest), encoding="utf-8")
    service = RecordingService(tmp_path / "new-store", tmp_path / "new-db.sqlite3")
    recording = service.register_wpf_recording(str(root))
    if montage:
        from app.services.acquisition_analysis_signals import AcquisitionSignal, AcquisitionSignalView
        names = recording.channels
        signals = tuple(AcquisitionSignal(n + "-AVG", n, names, "mean") for n in names)
        provenance = {"snapshot_sha256": "car-view", "formulas": [
            dict(name=s.name, positive=s.positive, negative_kind=s.kind, negative_labels=list(s.negatives))
            for s in signals]}
        service._recording_access._montage_snapshots.resolve = lambda *args: AcquisitionSignalView(signals, provenance)
        recording = replace(recording, stored_name=recording.stored_name.replace("wpf:", "wpf-montage:"),
                            channels=tuple(s.name for s in signals))
    filters = dict(low_hz=1, high_hz=50, notch_hz=50)
    automatic = ArtifactRemovalSpec(method="ica_infomax", allow_frontal_eog_proxy=True, ica_n_components=3)
    explicit = replace(automatic, eog_channels=("Fp1", "Fp2"))
    auto_report = inspect_recording_ica_components(service, recording, removal_spec=automatic, **filters)
    manual_report = inspect_recording_ica_components(service, recording, removal_spec=explicit, **filters)
    assert auto_report["channels"] == manual_report["channels"] == list(recording.channels)
    assert auto_report["n_components"] == manual_report["n_components"] == 3
    assert auto_report["eog_channels_used"] == manual_report["eog_channels_used"]
    assert auto_report["components"] == manual_report["components"]
    assert auto_report["recommended_exclude_components"] == manual_report["recommended_exclude_components"]
    outputs = []
    for spec, report in ((automatic, auto_report), (explicit, manual_report)):
        outputs.append(service.offline_scientific_preprocessing.prepare_continuous(recording, **filters,
            artifact_removal_spec=replace(spec, ica_fit_fingerprint=report["ica_fit_fingerprint"], ica_exclude_components=(0,))))
    np.testing.assert_array_equal(outputs[0].data, outputs[1].data)
    assert outputs[0].channel_names == outputs[1].channel_names == recording.channels


def test_identity_persists_and_changes_scientific_cache_without_changing_raw_samples(tmp_path):
    service, recording, values = make_recording(tmp_path)
    filters = dict(low_hz=1, high_hz=50, notch_hz=50)
    before_provenance = service.analysis_signal_provenance(recording)
    original = service.offline_scientific_preprocessing.prepare_continuous(recording, **filters)
    with service.open_access_session(recording) as pending:
        save(service, recording, F3="eog")
        with pytest.raises(ValueError, match="CHANNEL_IDENTITY_CHANGED"):
            pending.read_samples(0, 10)
    restarted = RecordingService(service.storage_dir, service.database_path)
    restored = restarted.require_recording(recording.id)
    assert source_channel_identities(restarted.storage_dir, restored)["F3"] == ("eog", "user_confirmed")
    assert restarted.analysis_signal_provenance(restored) != before_provenance
    after = restarted.offline_scientific_preprocessing.prepare_continuous(restored, **filters)
    assert after.channel_names == ("F4", "C3", "C4")
    assert after.source_identity != original.source_identity
    assert restarted.offline_scientific_preprocessing.prepare_continuous(restored, **filters) is after
    with restarted.open_access_session(restored, scientific_channels=False) as raw:
        assert raw.channel_names == recording.channels
        np.testing.assert_array_equal(raw.read_samples(0, 50).values_v, values[:50, :4])


def test_auxiliary_can_be_confirmed_as_eeg_without_changing_scoring_selection(tmp_path):
    service, recording, values = make_recording(tmp_path)
    save(service, recording, AUX_Eye="eeg")
    with service.open_access_session(service.require_recording(recording.id)) as session:
        assert session.channel_names == (*recording.channels, "AUX_Eye")
        np.testing.assert_array_equal(session.read_samples(0, 40).values_v, values[:40, :5])
    restored = service.require_recording(recording.id)
    from app.services.recording_channel_roles import scientific_channel_names
    assert scientific_channel_names(service, restored) == session.channel_names
    filters = dict(low_hz=1, high_hz=50, notch_hz=50)
    prepared = service.offline_scientific_preprocessing.prepare_continuous(restored, **filters)
    assert prepared.channel_names == session.channel_names
    assert service.offline_scientific_preprocessing.prepare_continuous(restored, **filters) is prepared


@pytest.mark.parametrize("bad", ["missing", "duplicate", "hash"])
def test_identity_update_validates_source_and_channels_atomically(tmp_path, bad):
    service, recording, _ = make_recording(tmp_path)
    roles = [ChannelIdentitySelection(name="F3", identity="eog")]
    if bad == "missing":
        roles[0] = ChannelIdentitySelection(name="Counter", identity="ecg")
    if bad == "duplicate":
        roles.append(ChannelIdentitySelection(name="f3", identity="eeg"))
    with pytest.raises(ValueError, match="CHANNEL_IDENTITY"):
        save_recording_identities(service, recording, ChannelIdentityUpdate(
            source_sha256="x"*64 if bad == "hash" else recording.source_sha256, channels=roles))
    assert service.require_recording(recording.id).channel_identities == ()


@pytest.mark.parametrize("payload", [[], {"channels": [None]},
    {"schema_version": "unknown", "channels": []},
    {"schema_version": "recording-channel-identities-v1",
     "channels": [dict(name="F3", identity=[])]}])
def test_corrupt_persisted_identity_fails_with_structured_error(tmp_path, payload):
    from app.services.recording_channel_roles import identity_path
    service, recording, _ = make_recording(tmp_path)
    path = identity_path(service.storage_dir, recording)
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(payload, dict):
        payload["source_sha256"] = recording.source_sha256
    path.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(ValueError, match="CHANNEL_IDENTITY_INVALID"):
        service.require_recording(recording.id)


def test_scientific_comparison_reports_identity_exclusions_without_removal(tmp_path):
    from app.services.preprocessing_preview import build_preprocessing_preview_window
    service, recording, _ = make_recording(tmp_path)
    save(service, recording, F3="eog")
    recording = service.require_recording(recording.id)
    result = build_preprocessing_preview_window(service, recording, start_s=0, window_s=3)
    assert result["baseline"]["channels"] == ["F4", "C3", "C4"]
    assert result["processed"]["channels"] == result["baseline"]["channels"]
    assert result["processed"]["excluded_output_channels"] == ["F3"]
    assert result["enhanced_preprocessing_active"] is False
    assert result["processed"]["traces_uv"] == result["baseline"]["traces_uv"]


def test_queued_run_rejects_changed_roles_instead_of_publishing_under_old_identity(tmp_path):
    from app.models.run import RunCreateRequest, RunStatus
    from app.services.run_queue import PersistentRunQueue
    service, recording, _ = make_recording(tmp_path)
    queue = PersistentRunQueue(service, service.database_path, tmp_path / "artifacts")
    run = queue.enqueue(RunCreateRequest(recording_id=recording.id, analysis_type="spectrum",
        config={"channels": ["F4"], "time": {"start_s": 0, "end_s": 10}}))
    save(service, recording, F3="eog")
    completed = queue.process_next()
    assert completed.run_id == run.run_id
    assert completed.status is RunStatus.FAILED
    assert "CHANNEL_IDENTITY_CHANGED" in completed.error.message
    assert queue.list_artifacts(run.run_id) == []


def test_window_context_rejects_identity_update_before_using_old_result(tmp_path):
    from app.services.algorithm_recording_context import _RecordingAlgorithmContext
    service, recording, _ = make_recording(tmp_path)
    context = _RecordingAlgorithmContext(service, recording)
    context.validate_source_identity()
    save(service, recording, F3="eog")
    with pytest.raises(ValueError, match="CHANNEL_IDENTITY_CHANGED"):
        context.validate_source_identity()
    context.close()


def test_identity_change_during_reader_creation_cannot_bind_a_mixed_session(tmp_path, monkeypatch):
    service, recording, _ = make_recording(tmp_path)
    opening = service._recording_access._open_wpf
    changed = False
    def change_during_open(*args, **kwargs):
        nonlocal changed
        session = opening(*args, **kwargs)
        if not changed:
            changed = True
            save(service, recording, F3="eog")
        return session
    monkeypatch.setattr(service._recording_access, "_open_wpf", change_during_open)
    with pytest.raises(ValueError, match="CHANNEL_IDENTITY_CHANGED"):
        service.open_access_session(recording)


def test_channel_identity_http_save_reload_and_invalid_update_are_atomic(tmp_path):
    from fastapi.testclient import TestClient
    from app.main import app
    from app.services.audit import AuditService
    service, recording, _ = make_recording(tmp_path)
    app.state.recording_service = service
    app.state.audit_service = AuditService(service.database_path)
    client = TestClient(app)
    route = f"/api/recordings/{recording.id}/preprocessing/channel-identities"
    payload = dict(source_sha256=recording.source_sha256, channels=[dict(name="F3", identity="eog")])
    response = client.put(route, json=payload)
    assert response.status_code == 200, response.text
    assert response.json()["channels"][0]["identity"] == "eog"
    payload["source_sha256"] = "f" * 64
    assert client.put(route, json=payload).status_code == 422
    assert source_channel_identities(service.storage_dir, service.require_recording(recording.id))["F3"][0] == "eog"
    payload["source_sha256"] = recording.source_sha256
    payload["channels"] = [dict(name="F3", identity="unconfirmed")]
    assert client.put(route, json=payload).status_code == 200
    assert service.require_recording(recording.id).channel_identities == (("F3", "unconfirmed"),)


@pytest.mark.parametrize("entries,units", [
    ([{"SourceColumn": True, "Identity": "eeg"}], ["uV"]),
    ([{"SourceColumn": 0, "Identity": "eeg"}], ["count"]),
    ([{"SourceColumn": 0, "Identity": []}], ["uV"]),
    ([{"SourceColumn": 0, "Identity": "eeg"}] * 2, ["uV"]),
])
def test_imported_identity_metadata_rejects_invalid_columns_roles_and_nonvoltage(entries, units):
    from app.services.recording_channel_roles import validated_import_identities
    with pytest.raises(ValueError, match="CHANNEL_IDENTITY_INVALID"):
        validated_import_identities({"PhysiologicalIdentities": entries}, ["Counter"], units)


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_export_import_preserves_eeg_and_true_auxiliary_identities(tmp_path, format):
    service, recording, values = make_recording(tmp_path)
    root = tmp_path / "source"
    manifest = json.loads((root / "manifest.json").read_text())
    config = dict(Id="cap", DeviceSignature="cap", Channels=[
        dict(NativeChannelIndex=i, ExpectedKind=1, ElectrodeLabel=n, IsSelectedForDisplay=True, DisplayOrder=i)
        for i, n in enumerate(recording.channels)])
    manifest["EegSignalUnit"] = "V"
    manifest["HardwareConfiguration"] = dict(channel_configuration_snapshot_json=json.dumps(config))
    for i, row in enumerate(manifest["Channels"]):
        row["NativeChannelIndex"] = i
    (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    exporter_service = RecordingService(tmp_path / "export-source", tmp_path / "export-db.sqlite3")
    recording = exporter_service.register_wpf_recording(str(root))
    save(exporter_service, recording, F3="eog", AUX_Eye="eeg")
    recording = exporter_service.require_recording(recording.id)
    package = RecordingSignalExporter(tmp_path / "export").export(recording, format, Event())
    try:
        signal = package.directory / ("recording." + format)
        sidecar = json.loads(signal.with_suffix(signal.suffix + ".labels.json").read_text(encoding="utf-8"))
        assert len(sidecar["channels"]) == 6
        identity = dict(exported_signal_sha256=sidecar["exported_signal_sha256"],
            channel_configuration_snapshot_json=sidecar["hardware"]["channel_configuration_snapshot_json"],
            channels=sidecar["channels"], hardware=sidecar["hardware"])
        from test_recording_import import _client
        client, imported_service, _ = _client(tmp_path / "imported")
        result = client.post("/api/recordings/import", files={"file": (signal.name, signal.read_bytes())},
            data={"export_channel_identity_json": json.dumps(identity)})
        assert result.status_code == 201, result.text
        imported = imported_service.require_recording(result.json()["id"])
        catalogue = discover_artifact_references(imported_service, imported)
        assert [c["identity"] for c in catalogue["channels"]] == ["eog", "eeg", "eeg", "eeg", "eeg", "ecg"]
        with imported_service.open_access_session(imported) as session:
            assert session.channel_names == ("F4", "C3", "C4", "AUX_Eye")
        from app.services.imported_analysis_signals import validate_imported_raw_analysis_channels
        validate_imported_raw_analysis_channels(imported, ["AUX_Eye"])
        raw_response = client.get(f"/api/recordings/{imported.id}/raw-samples?start_sample=0&end_sample=50")
        assert raw_response.status_code == 200, raw_response.text
        assert len(raw_response.json()["channels"]) == 6
        with imported_service.open_access_session(imported, scientific_channels=False) as session:
            assert len(session.channel_names) == 6
            np.testing.assert_allclose(session.read_samples(0, 50).values_v, values[:50, :6], atol=5e-8, rtol=0)
    finally:
        package.close()

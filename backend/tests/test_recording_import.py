import asyncio
import hashlib
import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from threading import Event

import numpy as np
import pyedflib
import pytest
from fastapi.testclient import TestClient

from app.persistence import connect_database
from app.services.workspace.projects import ProjectService
from app.services.recordings.recordings import RecordingService
from recording_api_fixtures import recording_api_app


def _edf_bytes(path: Path, labels: tuple[str, str] = ("Fz", "Pz"), *, sample_frequency: int = 10,
               sample_count: int = 20) -> bytes:
    writer = pyedflib.EdfWriter(str(path), 2, pyedflib.FILETYPE_EDFPLUS)
    writer.setSignalHeaders([
        {"label": labels[0], "dimension": "uV", "sample_frequency": sample_frequency, "physical_min": -100,
         "physical_max": 100, "digital_min": -32768, "digital_max": 32767, "transducer": "", "prefilter": ""},
        {"label": labels[1], "dimension": "uV", "sample_frequency": sample_frequency, "physical_min": -100,
         "physical_max": 100, "digital_min": -32768, "digital_max": 32767, "transducer": "", "prefilter": ""},
    ])
    if sample_frequency == 10 and sample_count == 20:
        writer.writeSamples([np.arange(sample_count, dtype=np.float64),
                             np.arange(sample_count, dtype=np.float64) * 2])
    else:
        time = np.arange(sample_count, dtype=np.float64) / sample_frequency
        writer.writeSamples([np.sin(2 * np.pi * 10 * time) * 20,
                             np.sin(2 * np.pi * 8 * time) * 15])
    writer.close()
    return path.read_bytes()


def _channel_snapshot(labels: list[str]) -> str:
    return json.dumps({"Channels": [
        {"NativeChannelIndex": index, "ExpectedKind": 2, "ElectrodeLabel": label,
         "IsSelectedForDisplay": index == 0, "DisplayOrder": index}
        for index, label in enumerate(labels)
    ]})


def _client(tmp_path: Path) -> tuple[TestClient, RecordingService, ProjectService]:
    database = tmp_path / "platform.sqlite3"
    recordings = RecordingService(tmp_path / "recordings", database)
    app = recording_api_app(recordings)
    return TestClient(app), recordings, app.state.project_service


def test_standalone_import_streams_hash_and_preserves_source_channel_order(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    source = _edf_bytes(tmp_path / "source.edf")

    response = client.post("/api/recordings/import", files={"file": ("source.edf", source)})

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert payload["source_sha256"] == hashlib.sha256(source).hexdigest()
    assert payload["file_size_bytes"] == len(source)
    assert payload["channels"] == ["Fz", "Pz"]
    assert payload["channel_types"] == ["eeg", "eeg"]
    assert (recordings.storage_dir / payload["stored_name"]).read_bytes() == source


def test_project_import_creates_recording_session_and_subject_atomically(tmp_path: Path):
    client, recordings, projects = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/desktop-id", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "project.edf")

    response = client.post(f"/api/projects/{project_id}/recordings/import",
                           data={"new_subject_local_code": "S-01"}, files={"file": ("project.edf", source)})

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert payload["project_id"] == project_id
    assert payload["channels"] == ["Fz", "Pz"]
    assert client.get(f"/api/projects/{project_id}/sessions").json()[0]["session_id"] == payload["session_id"]
    assert client.get(f"/api/projects/{project_id}/recordings").json()["recording_ids"] == [payload["recording_id"]]
    assert len(recordings.list_recordings()) == 1
    assert [subject.local_code for subject in projects.list_subjects(project_id)] == ["S-01"]


def test_project_import_persists_exact_channel_binding_without_reordering_raw_samples(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/binding", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "bound.edf")
    response = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01",
              "channel_configuration_snapshot_json": _channel_snapshot(["pz", "FZ"])},
        files={"file": ("bound.edf", source)},
    )

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert json.loads(payload["import_channel_column_mapping_json"]) == [
        {"source_column": 0, "native_channel_index": 1},
        {"source_column": 1, "native_channel_index": 0},
    ]
    stored = recordings.require_recording(payload["recording_id"])
    assert stored.import_channel_configuration_snapshot_json == payload["import_channel_configuration_snapshot_json"]
    assert client.get(f"/api/recordings/{payload['recording_id']}").json()["import_channel_column_mapping_json"] == payload["import_channel_column_mapping_json"]
    samples = client.get(f"/api/recordings/{payload['recording_id']}/raw-samples",
                         params={"start_sample": 3, "end_sample": 4}).json()
    assert samples["channels"] == ["FZ", "pz"]
    assert samples["values_v"][0][0] == pytest.approx(3e-6, abs=1e-8)
    assert samples["values_v"][0][1] == pytest.approx(6e-6, abs=1e-8)


def test_project_import_restores_export_sidecar_identity_and_rejects_tampering(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/sidecar", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "aliases.edf", ("CH0001", "CH0002"))
    snapshot = {"Channels": [
        {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz", "IsSelectedForDisplay": True, "DisplayOrder": 0},
        {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz", "IsSelectedForDisplay": True, "DisplayOrder": 1},
    ]}
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
    }

    response = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("aliases.edf", source)})

    assert response.status_code == 201, response.json()
    payload = response.json()
    assert payload["channels"] == ["Fz", "Pz"]
    assert payload["import_channel_configuration_snapshot_json"]
    assert json.loads(payload["import_channel_column_mapping_json"]) == [
        {"source_column": 0, "native_channel_index": 8},
        {"source_column": 1, "native_channel_index": 3},
    ]
    assert payload["import_acquisition_montage_snapshot_json"] is None
    assert payload["import_montage_configuration_snapshot_json"] is None
    assert payload["import_montage_catalogue_snapshot_json"] is None
    assert json.loads(payload["import_channel_column_mapping_json"]) == [
        {"source_column": 0, "native_channel_index": 8},
        {"source_column": 1, "native_channel_index": 3},
    ]
    assert recordings.require_recording(payload["recording_id"]).channels == ("Fz", "Pz")
    assert recordings.require_recording(payload["recording_id"]).raw_channel_labels == ("CH0001", "CH0002")
    samples = client.get(f"/api/recordings/{payload['recording_id']}/raw-samples",
                         params={"start_sample": 3, "end_sample": 4}).json()
    assert samples["channels"] == ["Fz", "Pz"]
    assert samples["values_v"][0] == pytest.approx([3e-6, 6e-6], abs=1e-8)
    with recordings.open_access_session(recordings.require_recording(payload["recording_id"])) as access:
        algorithm_input = access.read_samples(3, 4, ("Fz",))
    assert algorithm_input.channel_names == ("Fz",)
    assert algorithm_input.values_v[0, 0] == pytest.approx(3e-6, abs=1e-8)
    identity["exported_signal_sha256"] = "0" * 64
    rejected = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-02", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("tampered.edf", source)})
    assert rejected.status_code == 422
    assert rejected.json()["code"] == "RECORDING_EXPORT_METADATA_MISMATCH"
    assert len(recordings.list_recordings()) == 1


def test_present_malformed_export_sidecar_rejects_without_publishing_recording_or_session(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/malformed-sidecar", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "malformed.edf", ("CH0001", "CH0002"))
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": {"Channels": [
            {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
        ]},
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
    }

    response = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("malformed.edf", source)})

    assert response.status_code == 422
    assert response.json()["code"] == "RECORDING_EXPORT_METADATA_INVALID"
    assert recordings.list_recordings() == []
    assert client.get(f"/api/projects/{project_id}/sessions").json() == []


def test_project_import_round_trips_montage_snapshots_into_algorithm_signal_window(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/montage-roundtrip", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "montage.edf", ("CH0001", "CH0002"))
    channel_snapshot = {
        "Id": "channel-config", "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz",
             "IsSelectedForDisplay": True, "DisplayOrder": 1},
        ],
    }
    montage = {
        "Id": "saved-montage", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": [],
        "DerivedChannels": [{"Name": "Fz-REF", "PositiveLabel": "Fz", "NegativeKind": 0,
                             "NegativeLabels": [], "DisplayOrder": 0}],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "saved-montage"},
            "montage_configuration_snapshot_json": montage,
            "montage_catalogue_snapshot_json": [montage],
        },
    }

    response = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("montage.edf", source)})

    assert response.status_code == 201, response.json()
    imported = response.json()
    assert json.loads(imported["import_channel_configuration_snapshot_json"]) == channel_snapshot
    assert json.loads(imported["import_acquisition_montage_snapshot_json"]) == {"Id": "saved-montage"}
    assert json.loads(imported["import_montage_configuration_snapshot_json"]) == montage
    assert json.loads(imported["import_montage_catalogue_snapshot_json"]) == [montage]
    recording = recordings.require_recording(imported["recording_id"])
    assert recording.source_sha256 == hashlib.sha256(source).hexdigest()
    assert recording.channels == ("Fz", "Pz")
    assert recording.analysis_channels == ("Fz", "Pz")
    assert recording.montage_catalogue[0]["id"] == "saved-montage"
    assert recording.montage_catalogue[0]["outputs"][0]["id"] == "Fz-REF"
    assert recordings.analysis_signal_provenance(recording)["signal_mode"] == "raw"
    # Raw waveform viewing on an imported recording with montage snapshots must
    # stay in raw source column order rather than forcing montage projection.
    raw_samples = client.get(
        f"/api/recordings/{imported['recording_id']}/raw-samples",
        params={"start_sample": 3, "end_sample": 4},
    )
    assert raw_samples.status_code == 200, raw_samples.text
    assert raw_samples.json()["channels"] == ["Fz", "Pz"]
    assert raw_samples.json()["values_v"][0] == pytest.approx([3e-6, 6e-6], abs=1e-8)
    selected = recordings.select_analysis_montage(recording, "saved-montage")
    assert recordings.analysis_signal_provenance(selected)["signal_mode"] == "acquisition_montage"
    with recordings.open_access_session(selected) as access:
        window = access.read_samples(3, 4)
    assert window.start_sample == 3 and window.end_sample == 4
    assert window.channel_names == ("Fz-REF",)
    assert window.channel_units == ("V",)
    assert window.values_v[0, 0] == pytest.approx(3e-6, abs=1e-8)


def test_imported_montage_runs_official_algorithm_on_projected_eeg_signal(tmp_path: Path):
    from app.models.run import RunCreateRequest
    from app.bootstrap import build_builtin_registry
    from app.services.analysis.runs.runs import RunService

    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/imported-rbp", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "algorithm.edf", ("CH0001", "CH0002"), sample_frequency=200,
                        sample_count=6000)
    channel_snapshot = {
        "Id": "channel-config", "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz",
             "IsSelectedForDisplay": True, "DisplayOrder": 1},
        ],
    }
    montage = {
        "Id": "saved-montage", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": [],
        "DerivedChannels": [{"Name": "Fz-REF", "PositiveLabel": "Fz", "NegativeKind": 0,
                             "NegativeLabels": [], "DisplayOrder": 0}],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "saved-montage"},
            "montage_configuration_snapshot_json": montage,
            "montage_catalogue_snapshot_json": [montage],
        },
    }
    imported = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("algorithm.edf", source)})
    assert imported.status_code == 201, imported.json()
    recording = recordings.require_recording(imported.json()["recording_id"])
    montage_descriptor = recording.montage_catalogue[0]

    run = RunService(recordings, recordings.database_path, tmp_path / "artifacts", algorithm_registry=build_builtin_registry()).create(
        RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
            "algorithm_id": "rbp", "channel": "Fz-REF", "mode": "static",
            "time": {"start_s": 0, "end_s": 30},
            "analysis_montage_id": montage_descriptor["id"],
            "analysis_montage_revision": montage_descriptor["revision"],
            "analysis_montage_fingerprint": montage_descriptor["fingerprint"],
            "analysis_montage_outputs": [{"role": "Fz", "output": "Fz-REF", "unit": "V"}],
        }))

    assert run.status in {"completed", "gate_failed"}, run.error
    assert run.channel_mapping["channels"] == ["Fz-REF"]
    assert run.reference["mode"] == "acquisition_montage_snapshot"


def test_imported_raw_mode_rejects_disabled_channel_and_allows_enabled_eeg(tmp_path: Path):
    from app.models.run import RunCreateRequest
    from app.bootstrap import build_builtin_registry
    from app.services.analysis.runs.runs import RunService

    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/imported-raw-guard", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "raw_guard.edf", ("CH0001", "CH0002"), sample_frequency=200, sample_count=6000)
    channel_snapshot = {
        "Id": "channel-config", "DeviceSignature": "ant-layout",
        "HardwareReferenceElectrodeLocation": "REF", "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {"NativeChannelIndex": 8, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
             "IsSelectedForDisplay": True, "DisplayOrder": 0},
            {"NativeChannelIndex": 3, "ExpectedKind": 2, "ElectrodeLabel": "Pz",
             "IsSelectedForDisplay": False, "DisplayOrder": 1},
        ],
    }
    montage = {
        "Id": "saved-montage", "DeviceSignature": "ant-layout",
        "ChannelConfigurationSnapshot": channel_snapshot, "AverageReferenceLabels": [],
        "DerivedChannels": [{"Name": "Pz-REF", "PositiveLabel": "Pz", "NegativeKind": 0,
                             "NegativeLabels": [], "DisplayOrder": 0}],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 8, "kind": "2"},
            {"export_label": "CH0002", "source_label": "Pz", "native_channel_index": 3, "kind": "2"},
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "saved-montage"},
            "montage_configuration_snapshot_json": montage,
            "montage_catalogue_snapshot_json": [montage],
        },
    }
    imported = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("raw_guard.edf", source)},
    )
    assert imported.status_code == 201, imported.json()
    recording = recordings.require_recording(imported.json()["recording_id"])
    assert recording.channels == ("Fz", "Pz")
    assert recording.analysis_channels == ("Fz",)
    assert recording.montage_catalogue[0]["available"] is False
    assert recording.montage_catalogue[0]["unavailable_reason"] == "required_input_channel_disabled"

    runs = RunService(recordings, recordings.database_path, tmp_path / "artifacts", algorithm_registry=build_builtin_registry())
    with pytest.raises(ValueError, match="RECORDING_CHANNEL_UNAVAILABLE"):
        runs.create(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
            "algorithm_id": "rbp", "channel": "Pz", "mode": "static", "time": {"start_s": 0, "end_s": 30},
        }))
    with pytest.raises(ValueError, match="ANALYSIS_MONTAGE_UNAVAILABLE"):
        runs.create(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
            "algorithm_id": "rbp", "channel": "Pz-REF", "mode": "static", "time": {"start_s": 0, "end_s": 30},
            "analysis_montage_id": recording.montage_catalogue[0]["id"],
            "analysis_montage_revision": recording.montage_catalogue[0]["revision"],
            "analysis_montage_fingerprint": recording.montage_catalogue[0]["fingerprint"],
            "analysis_montage_outputs": [{"role": "Pz", "output": "Pz-REF", "unit": "V"}],
        }))
    raw_run = runs.create(RunCreateRequest(recording_id=recording.id, analysis_type="official_algorithm", config={
        "algorithm_id": "rbp", "channel": "Fz", "mode": "static", "time": {"start_s": 0, "end_s": 30},
    }))
    assert raw_run.status in {"completed", "gate_failed"}, raw_run.error
    assert raw_run.channel_mapping["channels"] == ["Fz"]
    assert raw_run.reference["mode"] == "original_recording_no_software_rereference"


def test_project_import_accepts_unlabeled_disabled_hardware_channel(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/disabled-channel", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "disabled.edf", ("CH0001", "CH0002"))
    snapshot = {"Channels": [
        {"NativeChannelIndex": 4, "ExpectedKind": 2, "ElectrodeLabel": "Fz",
         "IsSelectedForDisplay": True, "DisplayOrder": 0},
        {"NativeChannelIndex": 7, "ExpectedKind": 2, "ElectrodeLabel": "",
         "IsSelectedForDisplay": False, "DisplayOrder": 1},
    ]}
    identity = {
        "exported_signal_sha256": hashlib.sha256(source).hexdigest(),
        "channel_configuration_snapshot_json": snapshot,
        "channels": [
            {"export_label": "CH0001", "source_label": "Fz", "native_channel_index": 4, "kind": "2"},
            {"export_label": "CH0002", "source_label": None, "native_channel_index": 7, "kind": "2"},
        ],
    }

    response = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01", "export_channel_identity_json": json.dumps(identity)},
        files={"file": ("disabled.edf", source)})

    assert response.status_code == 201, response.json()
    recording = recordings.require_recording(response.json()["recording_id"])
    assert recording.channels == ("Fz", "CH0002")
    assert json.loads(recording.import_channel_column_mapping_json) == [
        {"source_column": 0, "native_channel_index": 4},
        {"source_column": 1, "native_channel_index": 7},
    ]


def test_import_binding_is_omitted_for_missing_or_ambiguous_labels(tmp_path: Path):
    from app.services.recordings.recording_import import _bind_channel_configuration

    labels, types = ["Fz", "Pz"], ["eeg", "eeg"]
    missing, missing_map = _bind_channel_configuration(_channel_snapshot(["Fz", "Cz"]), labels, types)
    ambiguous_snapshot = _channel_snapshot(["Fz", "Pz", "Pz"])
    ambiguous, ambiguous_map = _bind_channel_configuration(ambiguous_snapshot, labels, types)
    exact, exact_map = _bind_channel_configuration(_channel_snapshot(["Fz", "Pz"]), labels, types)

    assert (missing, missing_map) == (None, None)
    assert (ambiguous, ambiguous_map) == (None, None)
    assert exact is not None and json.loads(exact_map) == [
        {"source_column": 0, "native_channel_index": 0},
        {"source_column": 1, "native_channel_index": 1},
    ]


def test_import_binding_skips_non_eeg_columns_and_rejects_duplicate_native_identity():
    from app.services.recordings.recording_import import RecordingImportError, _bind_channel_configuration

    snapshot = _channel_snapshot(["Fz"])
    bound, mapping = _bind_channel_configuration(snapshot, ["Fz", "Status"], ["eeg", "stim"])
    assert bound == snapshot
    assert json.loads(mapping) == [{"source_column": 0, "native_channel_index": 0}]

    duplicate = json.dumps({"Channels": [
        {"NativeChannelIndex": 0, "ExpectedKind": 2, "ElectrodeLabel": label,
         "IsSelectedForDisplay": True, "DisplayOrder": index}
        for index, label in enumerate(["Fz", "Pz"])
    ]})
    with pytest.raises(RecordingImportError, match="无效 EEG 通道"):
        _bind_channel_configuration(duplicate, ["Fz", "Pz"], ["eeg", "eeg"])


def test_cross_project_subject_and_malformed_file_leave_no_partial_records(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    first = client.put("/api/projects/desktop/first", json={"name": "First"}).json()["project_id"]
    second = client.put("/api/projects/desktop/second", json={"name": "Second"}).json()["project_id"]
    subject = client.post(f"/api/projects/{first}/subjects", json={"local_code": "S-01"}).json()["subject_id"]

    crossed = client.post(f"/api/projects/{second}/recordings/import", data={"subject_id": subject},
                          files={"file": ("valid.edf", _edf_bytes(tmp_path / "valid.edf"))})
    malformed = client.post(f"/api/projects/{second}/recordings/import", data={"new_subject_local_code": "S-02"},
                            files={"file": ("bad.edf", b"not edf")})

    assert crossed.status_code == 409 and crossed.json()["code"] == "SUBJECT_PROJECT_MISMATCH"
    assert malformed.status_code == 422 and malformed.json()["code"] == "RECORDING_IMPORT_INVALID"
    assert recordings.list_recordings() == []
    assert client.get(f"/api/projects/{second}/sessions").json() == []
    assert [item["local_code"] for item in client.get(f"/api/projects/{second}/subjects").json()] == []
    assert not list(recordings.storage_dir.glob(".import-*.part"))


def test_existing_subject_code_reuses_identity_for_second_import(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/duplicate", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "duplicate.edf")
    first = client.post(f"/api/projects/{project_id}/recordings/import",
                        data={"new_subject_local_code": "S-01"}, files={"file": ("first.edf", source)})
    duplicate = client.post(f"/api/projects/{project_id}/recordings/import",
                            data={"new_subject_local_code": "S-01"}, files={"file": ("second.edf", source)})

    assert first.status_code == 201
    assert duplicate.status_code == 201
    assert duplicate.json()["subject_id"] == first.json()["subject_id"]
    assert len(recordings.list_recordings()) == 2
    assert len(client.get(f"/api/projects/{project_id}/sessions").json()) == 2
    assert len(client.get(f"/api/projects/{project_id}/subjects").json()) == 1
    assert len(list(recordings.storage_dir.glob("*.edf"))) == 2


def test_project_delete_removes_unreferenced_imported_recording(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/delete", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "delete.edf")
    imported = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01"}, files={"file": ("delete.edf", source)}).json()
    stored_path = recordings.storage_dir / recordings.require_recording(imported["recording_id"]).stored_name

    response = client.delete(f"/api/projects/{project_id}/sessions/{imported['session_id']}")

    assert response.status_code == 200, response.json()
    assert response.json()["recording_deleted"] is True
    assert client.get(f"/api/projects/{project_id}/sessions").json() == []
    assert client.get(f"/api/recordings/{imported['recording_id']}").status_code == 404
    assert not stored_path.exists()


def test_project_delete_preserves_recording_used_by_analysis(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/delete-used", json={"name": "Study"}).json()["project_id"]
    source = _edf_bytes(tmp_path / "used.edf")
    imported = client.post(f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-01"}, files={"file": ("used.edf", source)}).json()
    recording = recordings.require_recording(imported["recording_id"])
    stored_path = recordings.storage_dir / recording.stored_name
    with connect_database(recordings.database_path) as connection:
        connection.execute(
            "INSERT INTO event_markers (id, recording_id, time_s, label, duration_s, created_at) VALUES (?, ?, ?, ?, ?, ?)",
            ("event-1", recording.id, 1.0, "event", None, "2026-11-24T00:00:00Z"),
        )

    response = client.delete(f"/api/projects/{project_id}/sessions/{imported['session_id']}")

    assert response.status_code == 200, response.json()
    assert response.json()["recording_deleted"] is False
    assert client.get(f"/api/projects/{project_id}/sessions").json() == []
    assert recordings.require_recording(recording.id).id == recording.id
    assert stored_path.is_file()


def test_concurrent_imports_for_one_project_publish_independent_sessions(tmp_path: Path):
    client, recordings, _ = _client(tmp_path)
    project_id = client.put("/api/projects/desktop/concurrent", json={"name": "Study"}).json()["project_id"]
    subject_id = client.post(f"/api/projects/{project_id}/subjects", json={"local_code": "S-01"}).json()["subject_id"]
    first_source = _edf_bytes(tmp_path / "first.edf")
    second_source = _edf_bytes(tmp_path / "second.edf")

    def import_file(name: str, source: bytes) -> dict:
        with TestClient(client.app) as concurrent_client:
            response = concurrent_client.post(
                f"/api/projects/{project_id}/recordings/import",
                data={"subject_id": subject_id},
                files={"file": (name, source)},
            )
        assert response.status_code == 201, response.json()
        return response.json()

    with ThreadPoolExecutor(max_workers=2) as executor:
        results = list(executor.map(import_file, ["first.edf", "second.edf"], [first_source, second_source]))

    sessions = client.get(f"/api/projects/{project_id}/sessions").json()
    assert len({item["recording_id"] for item in results}) == 2
    assert len({item["session_id"] for item in results}) == 2
    assert {item["session_id"] for item in sessions} == {item["session_id"] for item in results}
    assert len(recordings.list_recordings()) == 2
    assert len(list(recordings.storage_dir.glob("*.edf"))) == 2


def test_raw_sample_range_returns_bounded_file_relative_volts_in_source_order(tmp_path: Path):
    client, _, _ = _client(tmp_path)
    source = _edf_bytes(tmp_path / "range.edf")
    imported = client.post("/api/recordings/import", files={"file": ("range.edf", source)}).json()

    response = client.get(f"/api/recordings/{imported['id']}/raw-samples",
                          params={"start_sample": 3, "end_sample": 6})
    too_large = client.get(f"/api/recordings/{imported['id']}/raw-samples",
                           params={"start_sample": 0, "end_sample": 2000000})

    assert response.status_code == 200, response.json()
    payload = response.json()
    assert (payload["start_sample"], payload["end_sample"]) == (3, 6)
    assert payload["channels"] == ["Fz", "Pz"]
    assert payload["channel_units"] == ["V", "V"]
    assert len(payload["values_v"]) == 3
    assert payload["values_v"][0][0] == pytest.approx(3e-6, abs=1e-8)
    assert payload["unavailable_intervals"] == []
    assert too_large.status_code == 422 and too_large.json()["code"] == "SAMPLE_RANGE_INVALID"


def test_stage_cancellation_removes_temporary_file(tmp_path: Path):
    _, recordings, _ = _client(tmp_path)
    from app.services.recordings.recording_import import EdfBdfImportService
    importer = EdfBdfImportService(recordings)
    cancel = asyncio.Event()

    async def chunks():
        yield b"part"
        cancel.set()
        yield b"more"

    async def check_cancelled():
        if cancel.is_set():
            raise RuntimeError("cancelled")

    try:
        asyncio.run(importer.stage_upload("cancel.edf", chunks(), check_cancelled))
        assert False, "cancellation must abort the staging operation"
    except RuntimeError as exc:
        assert str(exc) == "cancelled"
    assert not list(recordings.storage_dir.glob(".import-*.part.*"))


def test_stage_disk_write_failure_removes_partial_file(tmp_path: Path, monkeypatch):
    _, recordings, _ = _client(tmp_path)
    from app.services.recordings.recording_import import EdfBdfImportService
    importer = EdfBdfImportService(recordings)
    original_open = Path.open

    class FailingWriter:
        def __init__(self, file): self.file = file
        def __enter__(self): return self
        def __exit__(self, *args): return self.file.__exit__(*args)
        def write(self, chunk):
            self.file.write(chunk[:4])
            raise OSError("disk full")
        def __getattr__(self, name): return getattr(self.file, name)

    def open_with_failure(path, *args, **kwargs):
        file = original_open(path, *args, **kwargs)
        return FailingWriter(file) if path.name.startswith(".import-") else file

    monkeypatch.setattr(Path, "open", open_with_failure)

    async def chunks():
        yield b"file-data"

    async def never_cancel(): return None

    try:
        asyncio.run(importer.stage_upload("broken.edf", chunks(), never_cancel))
        assert False, "disk write failure must abort staging"
    except OSError as exc:
        assert str(exc) == "disk full"
    assert not list(recordings.storage_dir.glob(".import-*.part.*"))


def test_disconnect_cancels_background_publication_before_database_commit():
    from app.api.recording_uploads import publish_upload
    from app.services.recordings.recording_import import RecordingImportError

    class Request:
        async def is_disconnected(self): return True

    def publish(check_cancelled):
        while True:
            check_cancelled()

    try:
        asyncio.run(publish_upload(Request(), publish))
        assert False, "disconnect must cancel publication"
    except RecordingImportError as exc:
        assert exc.code == "RECORDING_IMPORT_CANCELLED"

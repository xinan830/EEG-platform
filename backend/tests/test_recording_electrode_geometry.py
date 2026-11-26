"""Recording geometry validation, spatial consistency and source/cache binding."""
from copy import deepcopy
import json
from types import SimpleNamespace

import mne
import numpy as np
import pytest

from app.scientific.electrode_geometry import (
    geometry_from_channel_snapshot, geometry_fingerprint, geometry_montage, geometry_positions,
    validate_geometry,
)
from app.scientific.bad_channels import build_spherical_spline_interpolator, apply_spherical_spline_interpolation
from app.services.electrode_locations import recording_map_coordinates
from app.services.signal_export_metadata import hardware_metadata
from app.services.recording_access import RecordingSourceIdentity
from app.services.recording_geometry import bind_session_geometry


@pytest.fixture
def geometry():
    sites = mne.channels.make_standard_montage("colin27_1020").get_positions()["ch_pos"]
    names = ("Fp1", "Fp2", "F3", "F4", "C3", "C4", "P3", "P4", "O1", "O2")
    return {
        "schema_version": "recording-electrode-geometry-v1",
        "coordinate_unit": "m", "coordinate_frame": "head",
        "source_id": "synthetic-test-array", "source_kind": "source_provided",
        "source_sha256": "a" * 64, "coordinate_transform": "fixture positions in head axes, m",
        "electrodes": [dict(label=f"X{i}", native_channel_index=i, position_m=sites[name].tolist())
                       for i, name in enumerate(names)],
    }


def snapshot_for(geometry):
    return {"Channels": [
        {"NativeChannelIndex": row["native_channel_index"], "ElectrodeLabel": row["label"],
         "ExpectedKind": 1, "DisplayOrder": i, "IsSelectedForDisplay": True}
        for i, row in enumerate(geometry["electrodes"])
    ], "ElectrodeGeometry": geometry}


@pytest.mark.parametrize("field,value", [
    ("coordinate_unit", "mm"), ("coordinate_frame", "mri"), ("source_sha256", "bad"),
    ("source_id", ""), ("source_kind", []), ("electrodes", []),
])
def test_invalid_geometry_rejected(geometry, field, value):
    geometry[field] = value
    with pytest.raises(ValueError, match="ELECTRODE_GEOMETRY_INVALID"):
        validate_geometry(geometry)


@pytest.mark.parametrize("field,value", [
    ("position_m", [0, 0, float("nan")]), ("position_m", [0, 0, 56]),
    ("position_m", [True, 0, .1]), ("native_channel_index", True), ("label", " X0"),
])
def test_invalid_electrode_rejected(geometry, field, value):
    geometry["electrodes"][0][field] = value
    with pytest.raises(ValueError, match="ELECTRODE_GEOMETRY_INVALID"):
        validate_geometry(geometry)


@pytest.mark.parametrize("field", ["label", "native_channel_index", "position_m"])
def test_duplicates_rejected(geometry, field):
    geometry["electrodes"][1][field] = geometry["electrodes"][0][field]
    with pytest.raises(ValueError, match="ELECTRODE_GEOMETRY_INVALID"):
        validate_geometry(geometry)


def test_native_binding_and_legacy_compatibility(geometry):
    assert geometry_from_channel_snapshot({}) is None
    snapshot = snapshot_for(geometry)
    assert geometry_from_channel_snapshot(snapshot) == validate_geometry(geometry)
    snapshot["Channels"][0]["ElectrodeLabel"] = "other"
    with pytest.raises(ValueError, match="ELECTRODE_GEOMETRY_INVALID"):
        geometry_from_channel_snapshot(snapshot)
    snapshot = snapshot_for(geometry)
    snapshot["Channels"][0]["ExpectedKind"] = 2
    with pytest.raises(ValueError, match="ELECTRODE_GEOMETRY_INVALID"):
        geometry_from_channel_snapshot(snapshot)


def test_custom_geometry_is_authoritative_and_subset_maps_stable(geometry):
    geometry["electrodes"][0]["label"] = "A1"
    custom = geometry_positions(geometry)
    np.testing.assert_array_equal(custom["a1"][1], geometry["electrodes"][0]["position_m"])
    assert "fp1" not in custom
    complete = recording_map_coordinates(geometry, [row["label"] for row in geometry["electrodes"]])
    assert recording_map_coordinates(geometry, ["A1"]) == {"a1": complete["a1"]}
    assert recording_map_coordinates(geometry, ["Fp1"]) == {}
    with pytest.raises(ValueError, match="BAD_CHANNEL_POSITION_UNAVAILABLE"):
        build_spherical_spline_interpolator(("Fp1", "X1", "X2", "X3"), ("Fp1",), electrode_geometry=geometry)


def test_custom_interpolation_matches_public_mne_and_preserves_healthy_and_gaps(geometry):
    names = tuple(row["label"] for row in geometry["electrodes"])
    data = np.random.default_rng(90).normal(0, 20e-6, (700, len(names)))
    plan = build_spherical_spline_interpolator(names, ("X5",), electrode_geometry=geometry)
    result = apply_spherical_spline_interpolation(data, plan)
    raw = mne.io.RawArray(data.T.copy(), mne.create_info(names, 250, "eeg"), verbose=False)
    raw.set_montage(geometry_montage(geometry))
    raw.info["bads"] = ["X5"]
    raw.interpolate_bads(mode="accurate", origin="auto", verbose=False)
    np.testing.assert_allclose(result, raw.get_data().T, rtol=0, atol=1e-12)
    np.testing.assert_array_equal(result[:, plan.good_indices], data[:, plan.good_indices])
    data[20:30, 0] = np.nan
    result = apply_spherical_spline_interpolation(data, plan)
    assert np.isnan(result[20:30, 5]).all()
    np.testing.assert_array_equal(result[:, 0], data[:, 0])


def test_import_validates_preserves_and_rejects_without_publication(tmp_path, geometry):
    from test_recording_import import _client, _edf_bytes
    client, recordings, _ = _client(tmp_path)
    geometry["electrodes"] = geometry["electrodes"][:2]
    snapshot = snapshot_for(geometry)
    source = _edf_bytes(tmp_path / "geometry.edf", ("X0", "X1"))
    response = client.post("/api/recordings/import",
        data={"channel_configuration_snapshot_json": json.dumps(snapshot)},
        files={"file": ("geometry.edf", source)})
    assert response.status_code == 201, response.text
    recording = recordings.require_recording(response.json()["id"])
    assert json.loads(recording.import_channel_configuration_snapshot_json)["ElectrodeGeometry"] == geometry
    assert recordings.analysis_signal_provenance(recording)["electrode_geometry"] == geometry
    exported = hardware_metadata({"HardwareConfiguration": {
        "channel_configuration_snapshot_json": json.dumps(snapshot)}})
    assert exported["channel_configuration_snapshot_json"]["ElectrodeGeometry"] == geometry
    with recordings.open_access_session(recording) as session:
        assert session.channel_names == ("X0", "X1")
        assert session.sample_count == 20
    for invalid in ("mm", "m"):
        snapshot["ElectrodeGeometry"]["coordinate_unit"] = invalid
        if invalid == "m":
            snapshot["Channels"][0]["ElectrodeLabel"] = "MISMATCH"
        rejected = client.post("/api/recordings/import",
            data={"channel_configuration_snapshot_json": json.dumps(snapshot)},
            files={"file": ("rejected.edf", source)})
        assert rejected.status_code == 422, rejected.text
        assert rejected.json()["code"] == "ELECTRODE_GEOMETRY_INVALID"
    assert len(recordings.list_recordings()) == 1


def test_geometry_changes_both_prepared_and_ica_identities(geometry, tmp_path):
    from app.services.ica_review_cache import compute_ica_fit_fingerprint
    from app.scientific.contracts.preprocessing import ArtifactRemovalSpec
    from app.services.prepared_source_identity import prepared_source_identity
    identity = RecordingSourceIdentity("recording", "a" * 64, "test", None)
    first = bind_session_geometry(SimpleNamespace(identity=identity), geometry).identity
    changed = deepcopy(geometry)
    changed["electrodes"][0]["position_m"][0] += .001
    second = bind_session_geometry(SimpleNamespace(identity=identity), changed).identity
    assert first.signal_view_sha256 != second.signal_view_sha256
    assert geometry_fingerprint(geometry) == geometry_fingerprint(validate_geometry(geometry))
    kwargs = dict(recording_id="recording", source_sha256=identity.source_sha256, channel_names=("X0",),
        sampling_rate_hz=250, sample_count=2000, low_hz=1, high_hz=45, notch_hz=50,
        bad_channel_spec=None, artifact_rejection_spec=None, removal_spec=ArtifactRemovalSpec(method="ica_infomax"))
    assert compute_ica_fit_fingerprint(signal_view_sha256=first.signal_view_sha256, **kwargs) != compute_ica_fit_fingerprint(signal_view_sha256=second.signal_view_sha256, **kwargs)
    record = SimpleNamespace(source_sha256=identity.source_sha256, sfreq=250, duration_s=8,
        channels=("X0",), raw_channel_labels=("X0",), canonical_channel_labels=("X0",),
        channel_types=("eeg",), channel_units=("V",), import_version="test")
    service = SimpleNamespace(analysis_signal_provenance=lambda _: {"electrode_geometry": geometry})
    before = prepared_source_identity(service, record, low_hz=1, high_hz=45, notch_hz=50, notch_q=30)
    service.analysis_signal_provenance = lambda _: {"electrode_geometry": changed}
    assert before != prepared_source_identity(service, record, low_hz=1, high_hz=45, notch_hz=50, notch_q=30)


@pytest.mark.parametrize("format", ["edf", "bdf"])
def test_export_import_preserves_recording_geometry(tmp_path, geometry, format):
    from threading import Event
    from test_recording_signal_export import source_fixture
    from test_recording_import import _client
    from app.services.recording_signal_export import RecordingSignalExporter
    from app.services.wpf_recording import inspect_wpf_recording
    source, _ = source_fixture(tmp_path / "source")
    geometry["electrodes"] = [dict(geometry["electrodes"][0], label="CH0002", native_channel_index=4)]
    snapshot = snapshot_for(geometry)
    snapshot["Channels"].append(dict(NativeChannelIndex=6, ElectrodeLabel="", ExpectedKind=2,
                                     DisplayOrder=1, IsSelectedForDisplay=False))
    manifest_path = tmp_path / "source/manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["HardwareConfiguration"]["channel_configuration_snapshot_json"] = json.dumps(snapshot)
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    source.source_sha256 = inspect_wpf_recording(manifest_path.parent).source_sha256
    package = RecordingSignalExporter(tmp_path / "exports").export(source, format, Event())
    try:
        signal = package.directory / f"recording.{format}"
        sidecar = json.loads(signal.with_name(signal.name + ".labels.json").read_text(encoding="utf-8"))
        identity = {key: sidecar[key] for key in ("exported_signal_sha256", "channels", "hardware")}
        identity["channel_configuration_snapshot_json"] = sidecar["hardware"]["channel_configuration_snapshot_json"]
        client, recordings, _ = _client(tmp_path)
        response = client.post("/api/recordings/import", data={"export_channel_identity_json": json.dumps(identity)},
            files={"file": (signal.name, signal.read_bytes())})
        assert response.status_code == 201, response.text
        recording = recordings.require_recording(response.json()["id"])
        assert json.loads(recording.import_channel_configuration_snapshot_json)["ElectrodeGeometry"] == geometry
    finally:
        package.close()


def test_iclabel_custom_geometry_retains_car_gates_and_exact_samples(geometry, monkeypatch):
    from app.scientific import ica_classifier as classifier
    names = tuple(f"{row['label']}-AVG" for row in geometry["electrodes"])
    samples = np.random.default_rng(22).normal(0, 20e-6, (len(names), 3000))
    samples -= samples.mean(axis=0)
    raw = mne.io.RawArray(samples.copy(), mne.create_info(names, 250, "eeg"), verbose=False)
    labels = [row["label"] for row in geometry["electrodes"]]
    provenance = {"electrode_geometry": geometry, "formulas": [
        dict(name=name, positive=label, negative_kind="mean", negative_labels=labels)
        for name, label in zip(names, labels)]}
    calls = []
    def inference(classified, *_args, **_kwargs):
        calls.append(True)
        np.testing.assert_array_equal(classified.get_data(), samples)
        positions = classified.get_montage().get_positions()["ch_pos"]
        for name, row in zip(names, geometry["electrodes"]):
            np.testing.assert_allclose(positions[name], row["position_m"], atol=1e-15)
        return np.tile([1., 0., 0., 0., 0., 0., 0.], (3, 1))
    metadata = dict(classifier_version="test", onnxruntime_version="test",
        iclabel_model_sha256="test", recommendation_rule_version="test")
    monkeypatch.setattr(classifier, "_load_iclabel_runtime", lambda: (inference, metadata))
    result = classifier.classify_ica_components(raw, SimpleNamespace(n_components_=3), names, 250, signal_provenance=provenance)
    assert result.classifier_status == "ready"
    assert len(calls) == 1
    provenance["electrode_geometry"] = deepcopy(geometry)
    provenance["electrode_geometry"]["electrodes"].pop()
    unavailable = classifier.classify_ica_components(raw, SimpleNamespace(n_components_=3), names, 250, signal_provenance=provenance)
    assert unavailable.classifier_unavailable_reason == "electrode_3d_coordinates_incomplete"
    provenance["formulas"][0]["negative_kind"] = "reference"
    unavailable = classifier.classify_ica_components(raw, SimpleNamespace(n_components_=3), names, 250, signal_provenance=provenance)
    assert unavailable.classifier_unavailable_reason == "non_average_reference_montage"
    assert len(calls) == 1


def test_component_preview_uses_same_custom_positions_without_standard_fallback(geometry):
    from app.services.preprocessing_preview import attach_ica_component_visuals
    names = ("X0", "X1", "X2", "Fp1")
    class IdentityIca:
        def get_components(self):
            return np.eye(4)
        def get_sources(self, raw):
            return raw
    fitted = SimpleNamespace(channel_names=names, sampling_rate_hz=250,
        info=mne.create_info(names, 250, "eeg"), ica=IdentityIca())
    samples = np.random.default_rng(4).normal(0, 20e-6, (500, 4))
    report = attach_ica_component_visuals({"components": [{"component_index": 0}]}, fitted, samples,
        signal_provenance={"electrode_geometry": geometry})
    coordinates = recording_map_coordinates(geometry, list(names))
    points = report["components"][0]["topomap"]["points"]
    for point in points:
        if point["channel"] == "Fp1":
            assert point["status"] != "included"
            assert point["x"] is None
        else:
            np.testing.assert_allclose([point["x"], point["y"]], coordinates[point["channel"].casefold()])


def test_custom_raw_and_montage_readers_apply_geometry_before_car(tmp_path, geometry):
    import hashlib
    from test_recording_import import _client
    from test_offline_bad_channels import _write_multi_channel_edf
    from app.scientific.contracts.preprocessing import BadChannelSpec
    client, recordings, _ = _client(tmp_path)
    labels = tuple(row["label"] for row in geometry["electrodes"])
    values = np.random.default_rng(5).normal(0, 15, (1000, len(labels)))
    source = _write_multi_channel_edf(tmp_path / "custom.edf", labels, values, sfreq=250)
    snapshot = snapshot_for(geometry)
    snapshot["Id"] = "custom-array"
    snapshot.update(DeviceSignature="custom-layout", HardwareReferenceElectrodeLocation="REF", HardwareGroundElectrodeLocation="GND")
    montage = {"Id": "custom-car", "DeviceSignature": "custom-layout", "ChannelConfigurationSnapshot": deepcopy(snapshot),
        "AverageReferenceLabels": list(labels), "DerivedChannels": [
            dict(Name=f"{label}-AVG", PositiveLabel=label, NegativeKind=2,
                 NegativeLabels=list(labels), DisplayOrder=i) for i, label in enumerate(labels)]}
    identity = dict(exported_signal_sha256=hashlib.sha256(source).hexdigest(),
        channel_configuration_snapshot_json=snapshot,
        channels=[dict(export_label=label, source_label=label, native_channel_index=i, kind="1")
                  for i, label in enumerate(labels)],
        hardware=dict(acquisition_montage_snapshot_json={"Id": "custom-car"}, montage_configuration_snapshot_json=montage))
    response = client.post("/api/recordings/import", data={"export_channel_identity_json": json.dumps(identity)},
        files={"file": ("custom.edf", source)})
    assert response.status_code == 201, response.text
    recording = recordings.require_recording(response.json()["id"])
    with recordings.open_access_session(recording, raw_unprojected=True) as session:
        raw = session.read_samples(0, 1000).values_v
    plan = build_spherical_spline_interpolator(labels, ("X5",), electrode_geometry=geometry)
    expected = apply_spherical_spline_interpolation(raw, plan)
    spec = BadChannelSpec(policy="interpolate", channels=("X5",))
    with recordings.open_access_session(recording, raw_unprojected=True, bad_channel_spec=spec) as session:
        np.testing.assert_allclose(session.read_samples(0,1000).values_v, expected, rtol=0, atol=1e-12)
    car = recordings.select_analysis_montage(recording, "custom-car")
    with recordings.open_access_session(car, bad_channel_spec=spec) as session:
        np.testing.assert_allclose(session.read_samples(0,1000).values_v, expected - expected.mean(axis=1,keepdims=True), rtol=0, atol=1e-12)
    provenance = recordings.analysis_signal_provenance(car)
    assert provenance["electrode_geometry"] == geometry
    assert provenance["map_positions"]["X0"]["canonical_site"] is None
    montage["ChannelConfigurationSnapshot"]["ElectrodeGeometry"]["electrodes"][0]["position_m"][0] += .001
    rejected = client.post("/api/recordings/import", data={"export_channel_identity_json": json.dumps(identity)},
        files={"file": ("contradictory.edf", source)})
    assert rejected.status_code == 422
    assert len(recordings.list_recordings()) == 1

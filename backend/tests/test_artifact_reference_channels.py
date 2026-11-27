"""Dedicated references survive source access without becoming EEG analysis outputs."""
import json
import struct
from dataclasses import replace

import numpy as np
import pytest

from app.scientific.contracts.artifact_removal import ArtifactRemovalSpec
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ScientificFilterSpec
from app.services.preprocessing.artifact_inspection import inspect_recording_ica_components
from app.services.recordings.recording_channel_roles import discover_artifact_references
from app.services.preprocessing.artifact_references import prepare_artifact_references
from app.services.preprocessing.mapped_arrays import MappedArrays
from app.services.recordings.recordings import RecordingService


def make_recording(tmp_path, *, reference_gap=False):
    root = tmp_path / "source"
    root.mkdir()
    rate, count = 250, 6000
    time = np.arange(count) / rate
    eye = sum(120e-6 * np.exp(-0.5 * ((time-c)/0.08)**2) for c in (2, 5, 8, 12, 16, 20))
    heart = sum(80e-6 * np.exp(-0.5 * ((time-c)/0.015)**2) for c in np.arange(0.5, 24, 1))
    rng = np.random.default_rng(31)
    brain = rng.normal(0, 3e-6, (count, 4))
    brain += 8e-6 * np.sin(2*np.pi*time[:, None] * [8, 10, 12, 18])
    eeg = brain + eye[:, None]*[1, 0.7, 0.2, 0.1] + heart[:, None]*[0.2, 0.4, 0.1, 0.3]
    values = np.column_stack((eeg, eye, heart, np.arange(count)))
    if reference_gap:
        values[2000:2100, 4] = np.nan
    labels = ["F3", "F4", "C3", "C4", "AUX_Eye", "AUX_Heart", "Counter"]
    manifest = dict(SessionId="reference-fixture", RecordingStartUtc="2026-11-26T00:00:00+00:00",
        SamplingRateHz=rate, Channels=[dict(StreamIndex=i, Label=name,
            Kind="Reference" if i < 4 else "Auxiliary" if i < 6 else "SampleCounter",
            Unit="V" if i < 6 else "count", PhysiologicalIdentity="eeg" if i < 4 else "eog" if i == 4 else "ecg" if i == 5 else "unconfirmed") for i, name in enumerate(labels)])
    (root/"manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    (root/"recording-summary.json").write_text(json.dumps(dict(status="completed")), encoding="utf-8")
    with (root/"samples-000001.bin").open("wb") as stream:
        stream.write(struct.pack("<qiiq", 0, count, 7, 0))
        values.astype("<f8").tofile(stream)
    service = RecordingService(tmp_path/"stored", tmp_path/"db.sqlite3")
    return service, service.register_wpf_recording(str(root)), values


def test_voltage_discovery_excludes_counter_and_preserves_exact_source_clock(tmp_path):
    service, recording, values = make_recording(tmp_path)
    assert recording.channels == ("F3", "F4", "C3", "C4")
    catalogue = discover_artifact_references(service, recording)
    assert [c["name"] for c in catalogue["channels"]] == ["F3", "F4", "C3", "C4", "AUX_Eye", "AUX_Heart"]
    with service.open_access_session(recording, raw_voltage_channels=True) as session:
        block = session.read_samples(123, 179, ("AUX_Heart", "AUX_Eye"))
        np.testing.assert_array_equal(block.values_v, values[123:179][:, [5, 4]])


@pytest.mark.parametrize("spec", [
    ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Counter",)),
    ArtifactRemovalSpec(method="ica_infomax", eog_channels=("Missing",)),
])
def test_unavailable_or_non_voltage_reference_fails_explicitly(tmp_path, spec):
    service, recording, _ = make_recording(tmp_path)
    owner = MappedArrays()
    try:
        with pytest.raises(ValueError, match="ARTIFACT_REFERENCE_UNAVAILABLE"):
            with prepare_artifact_references(service, recording, spec, sample_count=6000, sfreq=250,
                    filters=ScientificFilterSpec(1, 50, 50), allocate=owner.allocate, release=owner.release):
                pass
    finally:
        owner.close()


def test_role_overlap_and_multiple_ecg_rejected():
    with pytest.raises(ValueError, match="ARTIFACT_REFERENCE_INVALID"):
        ArtifactRemovalSpec(method="ica_infomax", eog_channels=(" eye ",), ecg_channels=("EYE",)).normalized()
    with pytest.raises(ValueError, match="ARTIFACT_REFERENCE_INVALID"):
        ArtifactRemovalSpec(method="ica_infomax", ecg_channels=("a", "b")).normalized()


def test_reference_allocation_failure_releases_already_allocated_map(tmp_path):
    service, recording, _ = make_recording(tmp_path)
    owner = MappedArrays()
    def allocate(shape):
        if owner.maps:
            raise MemoryError("reference budget")
        return owner.allocate(shape)
    try:
        with pytest.raises(MemoryError, match="reference budget"):
            with prepare_artifact_references(service, recording,
                    ArtifactRemovalSpec(method="ica_infomax", ecg_channels=("AUX_Heart",)),
                    sample_count=6000, sfreq=250, filters=ScientificFilterSpec(1, 50, 50),
                    allocate=allocate, release=owner.release):
                pass
        assert owner.maps == {}
    finally:
        owner.close()


def test_ecg_only_selection_does_not_require_frontal_proxy(tmp_path):
    service, recording, _ = make_recording(tmp_path)
    recording = replace(recording, channel_identities=(("AUX_Eye", "unconfirmed"),))
    report = inspect_recording_ica_components(service, recording, low_hz=1, high_hz=50, notch_hz=50,
        removal_spec=ArtifactRemovalSpec(method="ica_infomax", ecg_channels=("AUX_Heart",), ica_n_components=3))
    assert report["eog_channels_used"] == []
    assert report["ecg_channels_used"] == ["AUX_Heart"]
    assert all(row["ecg_score"] is not None for row in report["components"])


def test_dedicated_reference_ica_review_and_execution_reuse_without_eeg_pollution(tmp_path):
    service, recording, _ = make_recording(tmp_path)
    qc = ArtifactRejectionSpec(policy="reject", peak_to_peak_uv=300, muscle_zscore_threshold=None)
    spec = ArtifactRemovalSpec(method="ica_infomax", eog_channels=("AUX_Eye",),
        ecg_channels=("AUX_Heart",), ica_n_components=3)
    filters = dict(low_hz=1, high_hz=50, notch_hz=50)
    review = inspect_recording_ica_components(service, recording, removal_spec=spec,
        artifact_rejection_spec=qc, **filters)
    assert review["eog_channels_used"] == ["AUX_Eye"]
    assert review["ecg_channels_used"] == ["AUX_Heart"]
    assert review["channels"] == list(recording.channels)
    assert all(row["ecg_score"] is not None for row in review["components"])
    selected = max(review["components"], key=lambda row: abs(row["eog_score"]))["component_index"]
    applied = replace(spec, ica_fit_fingerprint=review["ica_fit_fingerprint"], ica_exclude_components=(selected,))
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    offline_prep = OfflineScientificPreprocessingService.for_recordings(service)
    output = offline_prep.prepare_continuous(recording,
        artifact_removal_spec=applied, artifact_rejection_spec=qc, **filters)
    assert output.channel_names == recording.channels
    assert output.artifact_removal_evidence["ica_model_reused"]
    assert output.artifact_removal_evidence["ecg_channels_used"] == ["AUX_Heart"]
    assert output.artifact_removal_evidence["excluded_components"] == [selected]
    baseline = offline_prep.prepare_continuous(recording, **filters)
    assert np.isfinite(output.data).all()
    assert not np.array_equal(output.data, baseline.data)
    automatic_ecg = offline_prep.prepare_continuous(recording,
        artifact_removal_spec=replace(applied, ecg_channels=()), artifact_rejection_spec=qc, **filters)
    assert automatic_ecg is output
    with pytest.raises(ValueError, match="ICA_FIT_FINGERPRINT_MISMATCH"):
        offline_prep.prepare_continuous(recording,
            artifact_removal_spec=replace(applied, eog_channels=("F3",)), artifact_rejection_spec=qc, **filters)


def test_selecting_existing_eeg_proxy_preserves_fitting_and_output(tmp_path):
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    service, recording, _ = make_recording(tmp_path)
    spec = ArtifactRemovalSpec(method="ica_infomax", eog_channels=("F3",), ica_n_components=3, ica_exclude_components=())
    output = OfflineScientificPreprocessingService.for_recordings(service).prepare_continuous(recording,
        low_hz=1, high_hz=50, notch_hz=50, artifact_removal_spec=spec)
    assert output.channel_names == ("F3", "F4", "C3", "C4")
    assert output.artifact_removal_evidence["eog_channels_used"] == ["F3"]


def test_reference_partition_precedes_car_and_bad_channel_exclusion_and_hits_cache(tmp_path):
    from app.scientific.contracts.preprocessing import BadChannelSpec
    from app.services.recordings.acquisition_analysis_signals import AcquisitionSignal, AcquisitionSignalView
    from app.services.recordings.recording_reference_sources import exclude_reference_outputs
    service, recording, values = make_recording(tmp_path)
    recording = replace(recording, channel_identities=(("F3", "eog"),))
    names = recording.channels
    signals = tuple(AcquisitionSignal(n+"-AVG", n, names, "mean") for n in names)
    provenance = {"snapshot_sha256": "original-view", "formulas": [
        dict(name=s.name, positive=s.positive, negative_kind=s.kind, negative_labels=list(s.negatives))
        for s in signals]}
    view = AcquisitionSignalView(signals, provenance)
    service._recording_access._montage_snapshots.resolve = lambda *args: view
    recording = replace(recording, stored_name=recording.stored_name.replace("wpf:", "wpf-montage:"),
        channels=tuple(s.name for s in signals))
    spec = ArtifactRemovalSpec(method="eog_regression", eog_channels=("F3",))
    bad = BadChannelSpec(policy="exclude", channels=("C4",))
    with service.open_access_session(recording, artifact_reference_channels=("F3",), bad_channel_spec=bad) as session:
        assert session.channel_names == ("F4-AVG", "C3-AVG")
        actual = session.read_samples(0, 500).values_v
        np.testing.assert_allclose(actual[:, 0], (values[:500, 1]-values[:500, 2])/2, atol=1e-18)
    assert exclude_reference_outputs(recording.channels, names, ("F3",), provenance, bad) == session.channel_names
    from app.models.analysis_config import OfflinePreprocessingRequest
    from app.services.preprocessing.artifact_references import resolve_preprocessing_analysis_channels
    layout = resolve_preprocessing_analysis_channels(service, recording, OfflinePreprocessingRequest.model_validate({
        "artifact_removal": {"method": "eog_regression", "eog_channels": ["F3"]},
        "bad_channels": {"policy": "exclude", "channels": ["C4"]}}))
    assert tuple(layout["channels"]) == session.channel_names
    # The prediction must use the same adjusted formulas as real reading.
    service.analysis_signal_provenance = lambda _: provenance
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    pre = OfflineScientificPreprocessingService.for_recordings(service)
    first = pre.prepare_continuous(recording, low_hz=1, high_hz=50, notch_hz=50,
        artifact_removal_spec=spec, bad_channel_spec=bad)
    second = pre.prepare_continuous(recording, low_hz=1, high_hz=50, notch_hz=50,
        artifact_removal_spec=spec, bad_channel_spec=bad)
    assert first is second
    assert first.channel_names == session.channel_names


@pytest.mark.parametrize("extension", ["edf", "bdf"])
def test_imported_reference_api_review_preview_and_spectrum(tmp_path, extension):
    import pyedflib
    from test_recording_import import _client
    service, recording, values = make_recording(tmp_path)
    labels = (*recording.channels, "EOG1", "ECG1")
    path = tmp_path/f"auxiliary.{extension}"
    is_bdf = extension == "bdf"
    with pyedflib.EdfWriter(str(path), len(labels),
            file_type=pyedflib.FILETYPE_BDFPLUS if is_bdf else pyedflib.FILETYPE_EDFPLUS) as writer:
        writer.setSignalHeaders([dict(label=n, dimension="uV", sample_frequency=250,
            physical_min=-500, physical_max=500, digital_min=-8388608 if is_bdf else -32768,
            digital_max=8388607 if is_bdf else 32767, transducer="", prefilter="") for n in labels])
        writer.writeSamples([values[:, i]*1e6 for i in range(6)])
    client, _, _ = _client(tmp_path/"api")
    imported = client.post("/api/recordings/import", files={"file": (path.name, path.read_bytes())})
    assert imported.status_code == 201, imported.text
    recording_id = imported.json()["id"]
    route = f"/api/recordings/{recording_id}"
    saved = client.put(route+"/preprocessing/channel-identities", json=dict(source_sha256=imported.json()["source_sha256"],
        channels=[dict(name="EOG1", identity="eog"), dict(name="ECG1", identity="ecg")]))
    assert saved.status_code == 200, saved.text
    catalog = client.get(route+"/preprocessing/reference-channels")
    assert catalog.status_code == 200, catalog.text
    assert [c["name"] for c in catalog.json()["channels"]] == list(labels)
    reviewed = client.get(route+"/preprocessing/ica-components", params=dict(
        low_hz=1, high_hz=50, notch_hz=50, eog_channels="EOG1", ecg_channels="ECG1", ica_n_components=3,
        bad_channels="EOG1,ECG1", bad_channel_policy="interpolate"))
    assert reviewed.status_code == 200, reviewed.text
    review = reviewed.json()
    assert review["channels"] == list(labels[:4])
    assert review["ecg_channels_used"] == ["ECG1"]
    preprocessing = {"bad_channels": dict(policy="interpolate", channels=["EOG1", "ECG1"]),
        "artifact_removal": dict(method="ica_infomax", eog_channels=["EOG1"],
        ecg_channels=["ECG1"], ica_n_components=3, ica_exclude_components=[],
        ica_fit_fingerprint=review["ica_fit_fingerprint"])}
    preview = client.post(route+"/preprocessing/preview-window", json=dict(
        start_s=2, window_s=4, low_hz=1, high_hz=50, notch_hz=50, preprocessing=preprocessing))
    assert preview.status_code == 200, preview.text
    assert preview.json()["processed"]["channels"] == list(labels[:4])
    assert preview.json()["processed"]["ica_model_reused"]
    layout = client.post(route+"/preprocessing/analysis-channels", json=preprocessing)
    assert layout.status_code == 200, layout.text
    assert layout.json()["channels"] == review["channels"]
    inspection = client.get(route+"/preprocessing/artifact-segments", params=dict(
        duration_s=10, eog_channels="EOG1", ecg_channels="ECG1"))
    assert inspection.status_code == 200, inspection.text
    assert inspection.json()["detection_scope"]["channels"] == list(labels[:4])
    spectrum = client.post(route+"/spectrum/configured", json=dict(
        mode="static", time=dict(start_s=2, end_s=6), channels=layout.json()["channels"],
        preprocessing=preprocessing))
    assert spectrum.status_code == 200, spectrum.text
    removal_evidence = spectrum.json()["filter_contract"]["preprocessing"]["artifact_removal"]
    assert removal_evidence["ica_model_reused"]
    assert removal_evidence["ecg_channels_used"] == ["ECG1"]
    # Exercise the real desktop full/on-demand algorithm entry points with the resolved EEG layout.
    from app.bootstrap import build_builtin_registry
    from app.services.analysis.runs.runs import RunService
    from app.services.analysis.windows.on_demand_dynamic import DynamicWindowService
    from app.services.analysis.scientific_compute_coordinator import ScientificComputeCoordinator
    from test_on_demand_rbp_api import wait_window
    recordings = client.app.state.recording_service
    runs = RunService(recordings, recordings.database_path, tmp_path / "run-artifacts", algorithm_registry=build_builtin_registry())
    client.app.state.run_service = runs
    windows = DynamicWindowService(recordings, build_builtin_registry(), recordings.database_path,
        tmp_path / "window-artifacts", compute_coordinator=ScientificComputeCoordinator())
    client.app.state.dynamic_window_service = windows
    try:
        for algorithm in ("psd", "rbp"):
            config = dict(algorithm_id=algorithm, mode="dynamic", channels=layout.json()["channels"],
                time=dict(start_s=0, end_s=24), dynamic_window_s=5, refresh_step_s=1,
                low_hz=1, high_hz=50, notch_hz=50, preprocessing=preprocessing)
            if algorithm == "rbp":
                config["channel"] = "F3"
            full = client.post("/api/runs", json=dict(recording_id=recording_id,
                analysis_type="official_algorithm", config=config))
            assert full.status_code == 202, full.text
            assert full.json()["status"] == "completed", full.text
            ensured = client.post("/api/analysis-windows/ensure", json=dict(recording_id=recording_id,
                algorithm_id=algorithm, config=config, cursor_sample=2500, request_id=algorithm+"-refs"))
            assert ensured.status_code in {200, 202}, ensured.text
            window = wait_window(client, ensured.json()["window_id"])
            assert window["state"] == "ready", window
            scientific = json.loads(window["scientific_configuration_json"])
            assert scientific["context"]["signal_mapping"]["channels"] == list(labels[:4])
    finally:
        windows.close(wait=True)
    invalid = client.get(route+"/preprocessing/ica-components", params=dict(
        eog_channels="EOG1", ecg_channels="EOG1"))
    assert invalid.status_code == 422
    assert "ARTIFACT_REFERENCE_INVALID" in invalid.text


def test_reference_gap_preserved_without_poisoning_finite_runs_and_maps_released(tmp_path):
    service, recording, values = make_recording(tmp_path, reference_gap=True)
    owner = MappedArrays()
    spec = ArtifactRemovalSpec(method="ica_infomax", eog_channels=("AUX_Eye",))
    try:
        with prepare_artifact_references(service, recording, spec, sample_count=6000, sfreq=250,
                filters=ScientificFilterSpec(1, 50, 50), allocate=owner.allocate, release=owner.release) as refs:
            assert np.isnan(refs.values_v[2000:2100]).all()
            assert np.isfinite(refs.values_v[:2000]).all()
            assert np.isfinite(refs.values_v[2100:]).all()
        assert owner.maps == {}
    finally:
        owner.close()


def test_multiple_eog_ssp_uses_public_api_and_only_emits_eeg(tmp_path):
    service, recording, _ = make_recording(tmp_path)
    recording = replace(recording, channel_identities=(("AUX_Heart", "eog"),))
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    output = OfflineScientificPreprocessingService.for_recordings(service).prepare_continuous(recording,
        low_hz=1, high_hz=50, notch_hz=50,
        artifact_removal_spec=ArtifactRemovalSpec(method="ssp_eog", eog_channels=("AUX_Eye", "AUX_Heart")))
    assert output.channel_names == recording.channels
    assert output.artifact_removal_evidence["eog_channels_used"] == ["AUX_Eye", "AUX_Heart"]
    assert np.isfinite(output.data).all()


@pytest.mark.parametrize("mixed_rate", [False, True])
def test_edf_physical_units_and_native_rates_are_verified(tmp_path, mixed_rate):
    import mne
    import pyedflib
    from app.services.recordings.recording_reference_sources import voltage_source_indexes
    path = tmp_path/"physical-units.edf"
    rates = [250, 125 if mixed_rate else 250, 250]
    with pyedflib.EdfWriter(str(path), 3, pyedflib.FILETYPE_EDFPLUS) as writer:
        writer.setSignalHeaders([dict(label=n, dimension=u, sample_frequency=r,
            physical_min=-500, physical_max=500, digital_min=-32768, digital_max=32767,
            transducer="", prefilter="") for n, u, r in zip(("EEG", "Eye", "Counter"), ("uV", "mV", "count"), rates)])
        writer.writeSamples([np.zeros(r*4) for r in rates])
    with mne.io.read_raw_edf(path, preload=False, verbose=False) as raw:
        if mixed_rate:
            with pytest.raises(ValueError, match="ARTIFACT_REFERENCE_ALIGNMENT_INVALID"):
                voltage_source_indexes(raw, path)
        else:
            assert voltage_source_indexes(raw, path) == [0, 1]


def test_explicit_references_are_not_interpolation_donors(tmp_path):
    from app.scientific.contracts.preprocessing import BadChannelSpec
    service, recording, _ = make_recording(tmp_path)
    recording = replace(recording, channel_identities=(("F3", "eog"),))
    # Removing F3 leaves only two healthy scalp donors for C4: reject, never borrow F3 or AUX.
    with pytest.raises(ValueError, match="BAD_CHANNEL_INSUFFICIENT_GOOD_DONORS"):
        service.open_access_session(recording, artifact_reference_channels=("F3",),
            bad_channel_spec=BadChannelSpec(policy="interpolate", channels=("C4",)))


def test_export_aliases_resolve_to_canonical_montage_inputs_before_car(tmp_path):
    from test_offline_bad_channels import _write_multi_channel_edf
    from test_recording_import import _client
    from app.scientific.contracts.preprocessing import BadChannelSpec
    _, source_recording, values = make_recording(tmp_path)
    canonical = source_recording.channels
    labels = tuple(f"CH{i:04}" for i in range(4))
    data = _write_multi_channel_edf(tmp_path/"mapped.edf", labels, values[:, :4]*1e6, sfreq=250)
    client, service, _ = _client(tmp_path/"api")
    response = client.post("/api/recordings/import", files={"file": ("mapped.edf", data)})
    assert response.status_code == 201, response.text
    record = service.require_recording(response.json()["id"])
    snapshot = dict(Id="mapped-config", DeviceSignature="layout", HardwareReferenceElectrodeLocation="REF",
        HardwareGroundElectrodeLocation="GND", Channels=[dict(NativeChannelIndex=10+i, ExpectedKind=1,
            ElectrodeLabel=n, IsSelectedForDisplay=True, DisplayOrder=i) for i, n in enumerate(canonical)])
    montage = dict(Id="car", DeviceSignature="layout", ChannelConfigurationSnapshot=snapshot,
        AverageReferenceLabels=list(canonical), DerivedChannels=[dict(Name=n+"-AVG", PositiveLabel=n,
            NegativeKind=2, NegativeLabels=list(canonical), DisplayOrder=i) for i, n in enumerate(canonical)])
    record = replace(record, selected_montage_id="car", channels=tuple(n+"-AVG" for n in canonical),
        channel_identities=(("CH0000", "eog"),),
        import_channel_configuration_snapshot_json=json.dumps(snapshot),
        import_channel_column_mapping_json=json.dumps([dict(source_column=i,native_channel_index=10+i) for i in range(4)]),
        import_montage_configuration_snapshot_json=json.dumps(montage))
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    pre = OfflineScientificPreprocessingService.for_recordings(service)
    options = dict(low_hz=1, high_hz=50, notch_hz=50,
        artifact_removal_spec=ArtifactRemovalSpec(method="eog_regression", eog_channels=("CH0000",)),
        bad_channel_spec=BadChannelSpec(policy="exclude", channels=("C4", "F3", "CH0000")))
    output = pre.prepare_continuous(record, **options)
    assert output.channel_names == ("F4-AVG", "C3-AVG")
    assert output.artifact_removal_evidence["eog_channels_used"] == ["CH0000"]
    assert pre.prepare_continuous(record, **options) is output
    eeg_record = replace(record, channel_identities=())
    filters = dict(low_hz=1, high_hz=50, notch_hz=50)
    aliased = inspect_recording_ica_components(service, eeg_record, **filters,
        removal_spec=ArtifactRemovalSpec(method="ica_infomax", eog_channels=("CH0001",), ica_n_components=3))
    canonical_report = inspect_recording_ica_components(service, eeg_record, **filters,
        removal_spec=ArtifactRemovalSpec(method="ica_infomax", eog_channels=("F4",), ica_n_components=3))
    assert aliased["channels"] == canonical_report["channels"] == list(eeg_record.channels)
    assert aliased["eog_channels_used"] == canonical_report["eog_channels_used"] == ["F4-AVG"]
    assert aliased["components"] == canonical_report["components"]

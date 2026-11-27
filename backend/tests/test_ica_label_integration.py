"""Integration tests for broadband Extended Infomax + ONNX ICLabel across review and execution."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import mne
import numpy as np
import pyedflib
import pytest
from scipy import signal

from app.scientific.artifact_removal import apply_offline_artifact_removal, fit_ica_model
from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.scientific.primitives import detrend_and_filter_for_inspection
from app.services.preprocessing.artifact_inspection import inspect_recording_ica_components
from app.services.recordings.recordings import RecordingService


_STANDARD_19 = (
    "Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8",
    "T7", "C3", "Cz", "C4", "T8",
    "P7", "P3", "Pz", "P4", "P8", "O1", "O2",
)


def _synthesize_multiclass_artifact_eeg(*, sfreq: float = 250.0, duration_s: float = 24.0, seed: int = 2026) -> np.ndarray:
    """Return raw (samples, 19) EEG in SI volts with alpha, blinks, single-channel pops, and 60-90 Hz EMG."""
    rng = np.random.default_rng(seed)
    n_samples = int(round(duration_s * sfreq))
    t = np.arange(n_samples, dtype=np.float64) / sfreq
    data = rng.normal(0.0, 4e-6, size=(n_samples, len(_STANDARD_19)))

    # Posterior 10 Hz alpha brain activity
    alpha = 20e-6 * np.sin(2.0 * np.pi * 10.0 * t)
    alpha_weights = np.array([0.1, 0.1, 0.2, 0.2, 0.25, 0.2, 0.2, 0.3, 0.4, 0.45, 0.4, 0.3, 0.7, 0.85, 0.85, 0.85, 0.7, 1.0, 1.0])
    data += alpha[:, None] * alpha_weights[None, :]

    # Strong frontal eye blinks (Fp1/Fp2)
    blink = np.zeros(n_samples, dtype=np.float64)
    for center in np.arange(1.8, duration_s - 1.5, 2.7):
        blink += 220e-6 * np.exp(-0.5 * ((t - center) / 0.085) ** 2)
    blink_weights = np.array([1.0, 0.96, 0.45, 0.42, 0.38, 0.42, 0.45, 0.15, 0.12, 0.12, 0.12, 0.15, 0.06, 0.05, 0.05, 0.05, 0.06, 0.03, 0.03])
    data += blink[:, None] * blink_weights[None, :]

    # Localized single-channel electrode noise on F8 (index 6)
    pop_noise = rng.normal(0.0, 35e-6, size=n_samples)
    sos_pop = signal.butter(3, [1.5, 40.0], btype="bandpass", fs=sfreq, output="sos")
    data[:, 6] += signal.sosfiltfilt(sos_pop, pop_noise)

    # High-frequency broadband muscle activity (55-95 Hz) localized at T8 (index 11)
    emg_raw = rng.normal(0.0, 45e-6, size=n_samples)
    sos_emg = signal.butter(4, [55.0, 95.0], btype="bandpass", fs=sfreq, output="sos")
    data[:, 11] += signal.sosfiltfilt(sos_emg, emg_raw)
    return data


def _write_edf(path: Path, data_v: np.ndarray, channel_names: tuple[str, ...], sfreq: float) -> None:
    data_uv = data_v * 1e6
    with pyedflib.EdfWriter(str(path), len(channel_names), file_type=pyedflib.FILETYPE_EDFPLUS) as writer:
        headers = []
        for idx, ch in enumerate(channel_names):
            col = data_uv[:, idx]
            pmax = max(500.0, float(np.ceil(np.max(np.abs(col)) * 1.25)))
            headers.append({
                "label": ch,
                "dimension": "uV",
                "sample_frequency": sfreq,
                "physical_max": pmax,
                "physical_min": -pmax,
                "digital_max": 32767,
                "digital_min": -32768,
            })
        writer.setSignalHeaders(headers)
        writer.writeSamples([np.ascontiguousarray(data_uv[:, i], dtype=np.float64) for i in range(len(channel_names))])


def _car_provenance(channel_names: tuple[str, ...]) -> dict[str, object]:
    return {
        "signal_mode": "montage_projected",
        "analysis_reference": "common_average",
        "formulas": [
            {"name": ch, "positive": ch, "negative_kind": "mean", "negative_labels": list(channel_names)}
            for ch in channel_names
        ],
    }


def _import_with_car_and_ref_montages(tmp_path: Path, data_v: np.ndarray, sfreq: float):
    from test_recording_import import _client

    edf_path = tmp_path / "synthetic_19ch.edf"
    _write_edf(edf_path, data_v, _STANDARD_19, sfreq)
    raw_bytes = edf_path.read_bytes()
    client, service, _ = _client(tmp_path / "app")
    project_id = client.put("/api/projects/desktop/iclabel-study", json={"name": "ICLabel Study"}).json()["project_id"]

    channel_snapshot = {
        "Id": "ch-cfg-19",
        "DeviceSignature": "synthetic-19",
        "HardwareReferenceElectrodeLocation": "REF",
        "HardwareGroundElectrodeLocation": "GND",
        "Channels": [
            {
                "NativeChannelIndex": idx,
                "ExpectedKind": 1,
                "ElectrodeLabel": ch,
                "IsSelectedForDisplay": True,
                "DisplayOrder": idx,
            }
            for idx, ch in enumerate(_STANDARD_19)
        ],
    }
    car_montage = {
        "Id": "montage-car",
        "Name": "标准平均参考 (CAR)",
        "DeviceSignature": "synthetic-19",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": list(_STANDARD_19),
        "DerivedChannels": [
            {
                "Name": f"{ch}-AVG",
                "PositiveLabel": ch,
                "NegativeKind": 2,
                "NegativeLabels": list(_STANDARD_19),
                "DisplayOrder": idx,
            }
            for idx, ch in enumerate(_STANDARD_19)
        ],
    }
    ref_montage = {
        "Id": "montage-ref",
        "Name": "原始硬件参考 (REF)",
        "DeviceSignature": "synthetic-19",
        "ChannelConfigurationSnapshot": channel_snapshot,
        "AverageReferenceLabels": [],
        "DerivedChannels": [
            {
                "Name": f"{ch}-REF",
                "PositiveLabel": ch,
                "NegativeKind": 0,
                "NegativeLabels": [],
                "DisplayOrder": idx,
            }
            for idx, ch in enumerate(_STANDARD_19)
        ],
    }
    identity = {
        "exported_signal_sha256": hashlib.sha256(raw_bytes).hexdigest(),
        "channel_configuration_snapshot_json": channel_snapshot,
        "channels": [
            {"export_label": ch, "source_label": ch, "native_channel_index": idx, "kind": "1"}
            for idx, ch in enumerate(_STANDARD_19)
        ],
        "hardware": {
            "acquisition_montage_snapshot_json": {"Id": "montage-car"},
            "montage_configuration_snapshot_json": car_montage,
            "montage_catalogue_snapshot_json": [car_montage, ref_montage],
        },
    }
    imported = client.post(
        f"/api/projects/{project_id}/recordings/import",
        data={"new_subject_local_code": "S-ICLABEL", "export_channel_identity_json": json.dumps(identity)},
        files={"file": (edf_path.name, raw_bytes)},
    )
    assert imported.status_code == 201, imported.text
    rec = service.require_recording(imported.json()["recording_id"])
    return client, service, rec


def test_broadband_iclabel_detects_artifacts_while_main_output_stays_1_to_50_hz():
    sfreq = 250.0
    raw_v = _synthesize_multiclass_artifact_eeg(sfreq=sfreq, duration_s=24.0)
    car_v = raw_v - raw_v.mean(axis=1, keepdims=True)

    main_1_50, _ = detrend_and_filter_for_inspection(car_v, sfreq, low_hz=1.0, high_hz=50.0, notch_hz=50.0)
    fit_1_100, _ = detrend_and_filter_for_inspection(car_v, sfreq, low_hz=1.0, high_hz=100.0, notch_hz=None)

    # Inject a synchronized bad segment NaN mask on both main and broadband fit matrices
    main_1_50[1000:1150, :] = np.nan
    fit_1_100[1000:1150, :] = np.nan

    spec = ArtifactRemovalSpec(
        method="ica_infomax",
        allow_frontal_eog_proxy=True,
        ica_n_components=15,
        random_state=42,
    )
    fitted = fit_ica_model(
        main_1_50, sfreq, _STANDARD_19, spec,
        fit_values_v=fit_1_100,
        signal_provenance=_car_provenance(_STANDARD_19),
        fit_bandpass_hz=(1.0, 100.0),
        fit_band_limited_by_nyquist=False,
    )
    assert fitted.ica_algorithm == "extended_infomax"
    assert fitted.classifier_status == "ready"
    assert fitted.classifier_method == "mne_icalabel_onnx"
    assert fitted.fit_bandpass_hz == (1.0, 100.0)
    assert fitted.fit_band_limited_by_nyquist is False

    detected_labels = {
        row["iclabel_label"]
        for row in fitted.component_scores
        if row["recommended_exclude"]
    }
    # Must catch at least one genuine non-brain artifact class (eye blink / channel noise / muscle)
    assert len(detected_labels) >= 1
    assert "brain" not in detected_labels
    assert "other" not in detected_labels

    # Apply unmixing matrix back onto 1-50 Hz main signal
    result = apply_offline_artifact_removal(main_1_50, sfreq, _STANDARD_19, spec, prefitted_ica=fitted)
    assert result.applied is True
    assert result.classifier_status == "ready"
    # NaN gap preserved identically
    assert np.all(np.isnan(result.data_v[1000:1150, :]))
    assert result.application_validity is not None

    # Verify main output remains strictly band-limited to 1-50 Hz (zero broadband 60-95 Hz leakage)
    clean_seg = result.data_v[1500:, :]
    freqs, psd = signal.welch(clean_seg, fs=sfreq, nperseg=512, axis=0)
    in_band = float(np.trapezoid(psd[(freqs >= 2.0) & (freqs <= 40.0), :].mean(axis=1), freqs[(freqs >= 2.0) & (freqs <= 40.0)]))
    above_55 = float(np.trapezoid(psd[(freqs >= 60.0) & (freqs <= 95.0), :].mean(axis=1), freqs[(freqs >= 60.0) & (freqs <= 95.0)]))
    assert above_55 < in_band * 0.01


def test_review_and_continuous_execution_share_cache_on_car_and_fallback_on_ref(tmp_path):
    sfreq = 250.0
    raw_v = _synthesize_multiclass_artifact_eeg(sfreq=sfreq, duration_s=18.0, seed=77)
    client, service, rec = _import_with_car_and_ref_montages(tmp_path, raw_v, sfreq)

    # 1) CAR montage via HTTP REST endpoint -> ICLabel ready + cached fingerprint
    api_resp = client.get(
        f"/api/recordings/{rec.id}/preprocessing/ica-components",
        params={
            "montage_id": "montage-car",
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "artifact_policy": "reject",
            "peak_to_peak_uv": 500.0,
            "allow_frontal_eog_proxy": "true",
            "ica_n_components": 12,
        },
    )
    assert api_resp.status_code == 200, api_resp.text
    car_report = api_resp.json()
    assert car_report["ica_algorithm"] == "extended_infomax"
    assert car_report["classifier_status"] == "ready"
    assert car_report["classifier_method"] == "mne_icalabel_onnx"
    assert car_report["classifier_unavailable_reason"] is None
    assert car_report["fit_bandpass_hz"] == [1.0, 100.0]
    assert car_report["fit_band_limited_by_nyquist"] is False
    assert car_report["fit_notch_hz"] == 50.0
    assert car_report["ica_cache_hit"] is False
    fit_fp = car_report["ica_fit_fingerprint"]
    assert isinstance(fit_fp, str) and len(fit_fp) == 24
    assert all(row["iclabel_label"] is not None and row["iclabel_probabilities"] is not None for row in car_report["components"])

    # Second inspection hits cache immediately
    rem_spec = ArtifactRemovalSpec(method="ica_infomax", allow_frontal_eog_proxy=True, ica_n_components=12, random_state=42)
    art_spec = ArtifactRejectionSpec(policy="reject", peak_to_peak_uv=500.0, muscle_zscore_threshold=4.0)
    car_report_2 = inspect_recording_ica_components(
        service, rec,
        montage_id="montage-car",
        low_hz=1.0, high_hz=50.0, notch_hz=50.0,
        artifact_rejection_spec=art_spec,
        removal_spec=rem_spec,
    )
    assert car_report_2["ica_cache_hit"] is True
    assert car_report_2["ica_fit_fingerprint"] == fit_fp

    # Continuous execution with confirmed fingerprint reuses the exact cached ICA model
    confirmed_excl = tuple(car_report["recommended_exclude_components"]) or (0,)
    car_rec = service.select_analysis_montage(rec, "montage-car")
    from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
    offline_prep = OfflineScientificPreprocessingService.for_recordings(service)
    prepared_car = offline_prep.prepare_continuous(
        car_rec,
        low_hz=1.0, high_hz=50.0, notch_hz=50.0,
        preprocessing_spec=OfflinePreprocessingSpec(
            filter_spec=ScientificFilterSpec(1.0, 50.0, 50.0),
            artifact_rejection=art_spec,
            artifact_removal=ArtifactRemovalSpec(
                method="ica_infomax",
                allow_frontal_eog_proxy=True,
                ica_n_components=12,
                ica_exclude_components=confirmed_excl,
                ica_fit_fingerprint=fit_fp,
                random_state=42,
            ),
        ),
    )
    ev = prepared_car.artifact_removal_evidence
    assert ev is not None
    assert ev["ica_model_reused"] is True
    assert ev["ica_fit_fingerprint"] == fit_fp
    assert ev["classifier_status"] == "ready"
    assert ev["ica_algorithm"] == "extended_infomax"

    # 2) REF montage -> explicit graceful fallback to EOG/ECG correlation without error
    ref_resp = client.get(
        f"/api/recordings/{rec.id}/preprocessing/ica-components",
        params={
            "montage_id": "montage-ref",
            "low_hz": 1.0,
            "high_hz": 50.0,
            "notch_hz": 50.0,
            "allow_frontal_eog_proxy": "true",
            "ica_n_components": 12,
        },
    )
    assert ref_resp.status_code == 200, ref_resp.text
    ref_report = ref_resp.json()
    assert ref_report["classifier_status"] == "fallback_eog_ecg"
    assert ref_report["classifier_method"] == "eog_ecg_correlation"
    assert ref_report["classifier_unavailable_reason"] == "non_average_reference_montage"
    assert all(row["iclabel_label"] is None and row["iclabel_probabilities"] is None for row in ref_report["components"])
    assert len(ref_report["recommended_exclude_components"]) >= 1


def test_rank_capping_after_interpolation_and_car_preserves_rhythms_with_sensor_noise(record_property):
    ch21 = (
        "Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8",
        "T7", "C3", "Cz", "C4", "T8", "M1",
        "P7", "P3", "Pz", "P4", "P8", "M2", "O1", "O2",
    )
    sfreq = 250.0
    n_samples = int(20 * sfreq)
    rng = np.random.default_rng(123)
    t = np.arange(n_samples, dtype=np.float64) / sfreq
    # Brain rhythms plus independent white sensor noise: not a physiological clean-EEG accuracy fixture.
    data = rng.normal(0.0, 6e-6, size=(len(ch21), n_samples))
    alpha = 18e-6 * np.sin(2.0 * np.pi * 10.0 * t)
    beta = 7e-6 * np.sin(2.0 * np.pi * 19.0 * t + 0.4)
    data[-2:, :] += alpha
    data[8:11, :] += beta
    raw = mne.io.RawArray(data, mne.create_info(list(ch21), sfreq=sfreq, ch_types="eeg"), verbose=False)
    raw.set_montage(mne.channels.make_standard_montage("colin27_1020"))
    raw.info["bads"] = ["F3", "P4"]
    raw.interpolate_bads(reset_bads=True, verbose=False)
    raw.set_eeg_reference("average", verbose=False)
    car_21 = raw.get_data().T

    main_1_50, _ = detrend_and_filter_for_inspection(car_21, sfreq, low_hz=1.0, high_hz=50.0, notch_hz=50.0)
    fit_1_100, _ = detrend_and_filter_for_inspection(car_21, sfreq, low_hz=1.0, high_hz=100.0, notch_hz=None)

    # Default requests 20 components on 21 channels, but 2 interpolations + CAR leaves effective rank 18
    spec = ArtifactRemovalSpec(method="ica_infomax", allow_frontal_eog_proxy=True, random_state=42)
    fitted = fit_ica_model(
        main_1_50, sfreq, ch21, spec,
        fit_values_v=fit_1_100,
        signal_provenance=_car_provenance(ch21),
        fit_bandpass_hz=(1.0, 100.0),
    )
    assert fitted.effective_rank == 18
    assert fitted.n_components_requested == 20
    assert int(fitted.ica.n_components_) == 18
    assert fitted.rank_capped is True
    assert "rank_capped_to_effective_rank" in fitted.fit_warnings
    assert fitted.ica_converged is True
    assert fitted.onnxruntime_version
    assert fitted.iclabel_model_sha256 and len(fitted.iclabel_model_sha256) == 16

    # Clean EEG reverse check: brain & other components must NEVER be auto-recommended
    for row in fitted.component_scores:
        if row["iclabel_label"] in {"brain", "other"}:
            assert row["recommended_exclude"] is False

    result = apply_offline_artifact_removal(main_1_50, sfreq, ch21, spec, prefitted_ica=fitted)
    assert result.excluded_components == fitted.recommended_exclude_components
    assert len(result.excluded_components) < fitted.ica.n_components_
    assert all(0 <= index < fitted.ica.n_components_ for index in result.excluded_components)
    relative_change = np.linalg.norm(result.data_v - main_1_50) / np.linalg.norm(main_1_50)
    assert np.all(np.isfinite(result.data_v))
    record_property("recommended_exclude_components", list(result.excluded_components))
    record_property("relative_rms_change", float(relative_change))
    for label, frequency, channels in [("alpha", 10, [-2, -1]), ("beta", 19, [8, 9, 10])]:
        times_s = np.arange(500, len(main_1_50) - 500) / sfreq
        basis = np.exp(-2j * np.pi * frequency * times_s)
        before = np.abs(basis @ main_1_50[500:-500, channels])
        after = np.abs(basis @ result.data_v[500:-500, channels])
        retention = after / before
        record_property(f"{label}_amplitude_retention", retention.tolist())
        np.testing.assert_allclose(retention, 1, rtol=.1, atol=0)


def test_persisted_reviewed_ica_survives_memory_eviction_and_rejects_expired_model(tmp_path):
    sfreq = 250.0
    raw_v = _synthesize_multiclass_artifact_eeg(sfreq=sfreq, duration_s=18.0, seed=88)
    client, service, rec = _import_with_car_and_ref_montages(tmp_path, raw_v, sfreq)

    offline_prep = client.app.state.offline_scientific_preprocessing_service
    rem_spec = ArtifactRemovalSpec(method="ica_infomax", allow_frontal_eog_proxy=True, ica_n_components=10, random_state=42)
    report = inspect_recording_ica_components(
        service, rec, montage_id="montage-car", low_hz=1.0, high_hz=50.0, notch_hz=50.0, removal_spec=rem_spec,
        offline_preprocessing=offline_prep,
    )
    fit_fp = report["ica_fit_fingerprint"]
    assert isinstance(fit_fp, str)
    original_model = offline_prep.ica_review_cache.get(fit_fp, recording_id=rec.id)
    assert original_model is not None

    # Simulate memory LRU eviction / backend process restart (memory cleared, disk intact)
    offline_prep.ica_review_cache.clear()
    offline_prep.prepared_cache.clear_recording(rec.id)
    restored_model = offline_prep.ica_review_cache.get(fit_fp, recording_id=rec.id)
    assert restored_model is not None
    assert restored_model.fit_notch_hz == 50.0
    assert np.array_equal(original_model.ica.unmixing_matrix_, restored_model.ica.unmixing_matrix_)
    np.testing.assert_allclose(original_model.ica.mixing_matrix_, restored_model.ica.mixing_matrix_, rtol=1e-12, atol=1e-15)
    car_v = raw_v - raw_v.mean(axis=1, keepdims=True)
    apply_spec = ArtifactRemovalSpec(method="ica_infomax", ica_exclude_components=(0,))
    original_output = apply_offline_artifact_removal(
        car_v, sfreq, original_model.channel_names, apply_spec, prefitted_ica=original_model)
    restored_output = apply_offline_artifact_removal(
        car_v, sfreq, restored_model.channel_names, apply_spec, prefitted_ica=restored_model)
    np.testing.assert_allclose(original_output.data_v, restored_output.data_v, rtol=1e-12, atol=1e-15)

    # Execute after memory clear: MUST recover exact persisted model with ica_model_reused=True
    offline_prep.ica_review_cache.clear()
    car_rec = service.select_analysis_montage(rec, "montage-car")
    confirmed = OfflinePreprocessingSpec(
        filter_spec=ScientificFilterSpec(1.0, 50.0, 50.0),
        artifact_removal=ArtifactRemovalSpec(
            method="ica_infomax",
            allow_frontal_eog_proxy=True,
            ica_n_components=10,
            ica_exclude_components=(0,),
            ica_fit_fingerprint=fit_fp,
            random_state=42,
        ),
    )
    prepared = offline_prep.prepare_continuous(
        car_rec, low_hz=1.0, high_hz=50.0, notch_hz=50.0, preprocessing_spec=confirmed,
    )
    assert prepared.artifact_removal_evidence["ica_model_reused"] is True
    assert prepared.artifact_removal_evidence["ica_fit_fingerprint"] == fit_fp

    # Simulate true model expiration (both memory and persisted disk models invalidated/deleted)
    offline_prep.invalidate_recording(rec.id)
    with pytest.raises(ValueError, match="ICA_REVIEW_MODEL_EXPIRED"):
        offline_prep.prepare_continuous(
            car_rec, low_hz=1.0, high_hz=50.0, notch_hz=50.0, preprocessing_spec=confirmed,
        )

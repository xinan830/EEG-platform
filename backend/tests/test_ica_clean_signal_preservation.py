"""Fixed synthetic spatial-source controls, not a real-EEG false-positive estimate."""

from dataclasses import replace

import mne
import numpy as np
import pytest
from scipy import signal

from app.scientific.artifact_removal import apply_offline_artifact_removal, fit_ica_model
from app.scientific.contracts.preprocessing import ArtifactRemovalSpec, STANDARD_MONTAGE_NAME
from app.scientific.primitives import detrend_and_filter_for_inspection


CHANNELS = (
    "Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T7", "C3", "Cz",
    "C4", "T8", "P7", "P3", "Pz", "P4", "P8", "O1", "O2",
)
SFREQ = 250.0


def _spatial_brain_rhythms(seed):
    """Mix four oscillatory sources with smooth dipolar scalp fields, in sample-major V."""
    rng = np.random.default_rng(seed)
    time_s = np.arange(7500) / SFREQ
    coordinates = mne.channels.make_standard_montage(STANDARD_MONTAGE_NAME).get_positions()["ch_pos"]
    positions = np.array([coordinates[name] for name in CHANNELS])
    positions /= np.linalg.norm(positions, axis=1, keepdims=True)
    centers = np.array([[0, -.4, .3], [0, 0, .45], [-.25, .2, .3], [.3, -.2, .3]])
    directions = np.array([[0, -1, .5], [0, 0, 1], [-.5, .5, 1], [1, -.5, .5]])
    topographies = []
    for center, direction in zip(centers, directions, strict=True):
        displacement = positions - center
        weights = displacement @ direction / np.linalg.norm(displacement, axis=1) ** 3
        weights -= weights.mean()
        topographies.append(weights / np.max(np.abs(weights)))
    sources = []
    for frequency, amplitude in [(10, 25e-6), (19, 12e-6), (6, 15e-6), (12, 10e-6)]:
        envelope = 1 + .3 * np.sin(2 * np.pi * .13 * time_s + rng.uniform(0, 6))
        rhythm = amplitude * envelope * np.sin(2 * np.pi * frequency * time_s + rng.uniform(0, 6))
        rhythm += signal.sosfiltfilt(
            signal.butter(2, [1, 30], btype="bandpass", fs=SFREQ, output="sos"),
            rng.normal(0, amplitude * .15, len(time_s)))
        sources.append(rhythm)
    values_v = np.array(sources).T @ np.array(topographies)
    values_v += rng.normal(0, .1e-6, values_v.shape)
    values_v -= values_v.mean(axis=1, keepdims=True)
    return values_v


def _rhythm_power(values_v, frequency, channel_indices):
    interior = values_v[500:-500, channel_indices]
    frequencies, density = signal.welch(interior, fs=SFREQ, nperseg=1000, axis=0)
    return density[np.abs(frequencies - frequency) <= 1].sum(axis=0)


@pytest.mark.parametrize("seed", [2026, 42, 123])
def test_actual_iclabel_recommendations_preserve_clean_spatial_sources(seed, record_property):
    values_v = _spatial_brain_rhythms(seed)
    main_v, _ = detrend_and_filter_for_inspection(values_v, SFREQ, low_hz=1, high_hz=50, notch_hz=50)
    fit_v, _ = detrend_and_filter_for_inspection(values_v, SFREQ, low_hz=1, high_hz=100, notch_hz=None)
    provenance = {"formulas": [
        {"name": name, "positive": name, "negative_kind": "mean", "negative_labels": list(CHANNELS)}
        for name in CHANNELS
    ]}
    spec = ArtifactRemovalSpec(method="ica_infomax", ica_n_components=4)
    fitted = fit_ica_model(
        main_v, SFREQ, CHANNELS, spec, fit_values_v=fit_v,
        signal_provenance=provenance, fit_bandpass_hz=(1, 100))
    assert fitted.ica_converged and fitted.classifier_status == "ready"
    assert all(row["iclabel_probabilities"] is not None for row in fitted.component_scores)
    # No predicted label is used as ground truth: this input has no injected artifacts.
    assert fitted.recommended_exclude_components == (), fitted.component_scores
    record_property("recommended_exclude_components", list(fitted.recommended_exclude_components))
    result = apply_offline_artifact_removal(main_v, SFREQ, CHANNELS, spec, prefitted_ica=fitted)
    assert result.excluded_components == ()
    assert result.status == "skipped_no_artifact_components" and not result.applied
    assert np.all(np.isfinite(result.data_v))
    relative_error = np.linalg.norm(result.data_v - main_v) / np.linalg.norm(main_v)
    record_property("relative_rms_change", float(relative_error))
    assert relative_error < 1e-12
    for frequency, channels in [(10, [-2, -1]), (19, [8, 9, 10])]:
        retention = _rhythm_power(result.data_v, frequency, channels) / _rhythm_power(main_v, frequency, channels)
        record_property(f"{frequency}_hz_power_retention", retention.tolist())
        np.testing.assert_allclose(retention, 1, rtol=.05, atol=0)

    # Force removal of the strongest alpha component to prove the power check detects loss.
    source_raw = mne.io.RawArray(main_v.T, fitted.info, verbose=False)
    components = fitted.ica.get_sources(source_raw).get_data().T
    alpha_component = int(np.argmax(_rhythm_power(components, 10, slice(None))))
    removed = apply_offline_artifact_removal(
        main_v, SFREQ, CHANNELS, replace(spec, ica_exclude_components=(alpha_component,)), prefitted_ica=fitted)
    assert removed.applied
    alpha_retention = _rhythm_power(removed.data_v, 10, [-2, -1]) / _rhythm_power(main_v, 10, [-2, -1])
    record_property("forced_alpha_removal_power_retention", alpha_retention.tolist())
    assert np.all(alpha_retention < .5), alpha_retention

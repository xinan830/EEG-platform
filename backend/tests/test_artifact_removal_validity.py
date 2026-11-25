"""Removal gaps are reconstruction dependencies, not extra detected artifacts."""
from types import SimpleNamespace

import mne
import numpy as np
import pytest

from app.scientific.artifact_removal import apply_offline_artifact_removal
from app.scientific.artifact_removal_validity import build_application_validity, project_application_validity
from app.scientific.contracts.artifact_removal import ArtifactRemovalSpec


def test_exact_dependencies_and_clipped_channel_projection():
    before = np.ones((10, 3))
    before[0:2, 0] = np.nan
    before[1:3, 1] = np.inf
    before[8:] = np.nan
    after = before.copy()
    after[~np.isfinite(before).all(axis=1)] = np.nan
    evidence = build_application_validity(before, after, ("F3", "O1", "C4"), 10)
    assert [(r["start_sample"], r["end_sample"]) for r in evidence["intervals"]] == [(0, 1), (1, 2), (2, 3), (8, 10)]
    assert evidence["additional_unavailable_channel_samples"] == 5
    assert evidence["unavailable_sample_count"] == 5
    assert evidence["intervals"][1]["missing_input_channels"] == ["F3", "O1"]
    projected = project_application_validity(evidence, 1, 9, ["C4"])
    assert projected["additional_unavailable_channel_samples"] == 2
    assert projected["output_unavailable_channel_samples"] == 3
    assert projected["intervals"][0]["missing_input_channels"] == ["F3", "O1"]
    assert projected["intervals"][-1]["end_s"] == .9
    assert project_application_validity(evidence, 3, 8)["intervals"] == []


@pytest.fixture(scope="module")
def fitted():
    rng = np.random.default_rng(71)
    names = ("F3", "C4", "O1", "O2")
    values = rng.laplace(size=(1200, 4)) * 1e-5
    info = mne.create_info(list(names), 100, "eeg")
    raw = mne.io.RawArray(values.T, info, verbose=False)
    raw.filter(l_freq=1, h_freq=None, verbose=False)
    ica = mne.preprocessing.ICA(n_components=3, method="infomax", random_state=42, max_iter=50, verbose=False)
    ica.fit(raw, verbose=False)
    return values, SimpleNamespace(ica=ica, info=info, channel_names=names, sampling_rate_hz=100,
        eog_channels_used=(), eog_channel_source=None, ecg_channels_used=(),
        recommended_exclude_components=(0,), component_scores=())


@pytest.mark.parametrize("excluded", [(0,), ()])
def test_ica_gap_evidence_matches_actual_mask_and_independent_mne(fitted, excluded):
    values, model = fitted
    values = values.copy()
    values[20:30, 1] = np.nan
    values[1100:1110] = np.nan
    original = values.copy()
    result = apply_offline_artifact_removal(values, 100, model.channel_names,
        ArtifactRemovalSpec(method="ica_infomax", ica_exclude_components=excluded), prefitted_ica=model)
    validity = result.to_evidence()["application_validity"]
    np.testing.assert_array_equal(values, original)
    actual_extra = np.isfinite(values) & ~np.isfinite(result.data_v)
    assert validity["additional_unavailable_channel_samples"] == int(actual_extra.sum()) == (30 if excluded else 0)
    if excluded:
        finite = np.isfinite(values).all(axis=1)
        independent = mne.io.RawArray(values[finite].T.copy(), model.info, verbose=False)
        model.ica.copy().apply(independent, exclude=list(excluded), verbose=False)
        np.testing.assert_allclose(result.data_v[finite], independent.get_data().T, rtol=0, atol=1e-18)
        assert validity["intervals"][0]["additional_unavailable_channels"] == ["F3", "O1", "O2"]
    else:
        np.testing.assert_array_equal(values, result.data_v)


def test_disabled_and_empty_data_do_not_report_fabricated_gaps():
    result = apply_offline_artifact_removal(np.empty((0, 2)), 100, ("F3", "O1"), ArtifactRemovalSpec())
    assert result.to_evidence()["application_validity"] is None
    assert build_application_validity(result.data_v, result.data_v, ("F3", "O1"), 100)["intervals"] == []


def test_application_version_invalidates_results_without_invalidating_reviewed_fit(monkeypatch):
    from app.core.provenance import with_offline_preprocessing
    from app.scientific.contracts import artifact_removal
    from app.services.ica_review_cache import compute_ica_fit_fingerprint

    spec = ArtifactRemovalSpec(method="ica_infomax", ica_exclude_components=(0,))
    config = {"preprocessing": {"artifact_removal": {"method": "ica_infomax"}}}
    fit_args = dict(recording_id="rec", source_sha256="sha", signal_view_sha256=None,
        channel_names=("F3", "O1"), sampling_rate_hz=100, sample_count=1000,
        low_hz=1, high_hz=40, notch_hz=None, bad_channel_spec=None, artifact_rejection_spec=None, removal_spec=spec)
    before = spec.fingerprint(), with_offline_preprocessing({}, config), compute_ica_fit_fingerprint(**fit_args)
    monkeypatch.setattr(artifact_removal, "ARTIFACT_REMOVAL_APPLICATION_VERSION", "changed")
    assert spec.fingerprint() != before[0]
    assert with_offline_preprocessing({}, config) != before[1]
    assert compute_ica_fit_fingerprint(**fit_args) == before[2]
    assert with_offline_preprocessing({}, {}) == {}


def test_failed_spectral_gate_retains_reconstruction_gap_provenance():
    from app.services.analysis_preprocess_cache import PreprocessedRecording
    from app.services.spectral_analysis import SpectralAnalysisService
    from app.scientific.quality import SpectralQualityGateError
    from app.scientific.contracts.preprocessing import ArtifactRemovalSpec

    names = ("F3", "O1")
    before = np.zeros((1000, 2))
    before[:600, 0] = np.nan
    after = before.copy()
    after[:600] = np.nan
    removal = {"method": "ica_infomax", "application_validity": build_application_validity(before, after, names, 100)}
    prepared = PreprocessedRecording(after, 100, names, None, artifact_removal_evidence=removal)
    service = SpectralAnalysisService(SimpleNamespace(
        analysis_signal_provenance=lambda _: {"analysis_reference": "as_recorded"}), None)
    recording = SimpleNamespace(id="rec", duration_s=10)
    with pytest.raises(SpectralQualityGateError) as caught:
        service.load_spectrum(recording, start_s=0, window_s=4, channels=["O1"],
            low_hz=1, high_hz=40, notch_hz=None, _preprocessed=prepared,
            artifact_removal_spec=ArtifactRemovalSpec(method="ica_infomax"))
    evidence = caught.value.quality["evidence"]["preprocessing"]["artifact_removal"]["application_validity"]
    assert evidence["channels"] == ["O1"]
    assert evidence["additional_unavailable_channel_samples"] == 400
    assert evidence["intervals"][0]["missing_input_channels"] == ["F3"]


def test_faa_source_reports_external_missing_input_dependency():
    from app.services.analysis_preprocess_cache import PreprocessedRecording
    from app.services.algorithm_recording_context import _RecordingAlgorithmContext

    names = ("F3", "F4", "T5")
    before = np.zeros((1000, 3))
    before[100:150, 2] = np.nan
    after = before.copy()
    after[100:150] = np.nan
    prepared = PreprocessedRecording(after, 100, names, None, artifact_removal_evidence={
        "method": "ica_infomax", "application_validity": build_application_validity(before, after, names, 100)})
    context = _RecordingAlgorithmContext(SimpleNamespace(
        load_preprocessed_signals=lambda *a, **k: prepared,
        analysis_signal_provenance=lambda _: {"analysis_reference": "as_recorded"}),
        SimpleNamespace(id="rec", channels=names, sfreq=100, duration_s=10))
    left, right, _, evidence = context.load_faa_signals(
        start_s=1.2, end_s=2, f3_channel="F3", f4_channel="F4", low_hz=1, high_hz=40)
    assert np.isnan(left[:30]).all() and np.isnan(right[:30]).all()
    validity = evidence["artifact_removal"]["application_validity"]
    assert validity["additional_unavailable_channel_samples"] == 60
    assert validity["intervals"][0]["missing_input_channels"] == ["T5"]
    assert validity["intervals"][0]["unavailable_output_channels"] == ["F3", "F4"]


@pytest.mark.parametrize("method", ["eog_regression", "ssp_eog"])
def test_other_removal_methods_share_exact_mask_accounting(method):
    from test_offline_artifact_removal import _CHANNELS_10, _synthesize_eeg_with_blinks

    values, _, _ = _synthesize_eeg_with_blinks()
    values[500:540, 0] = np.nan
    result = apply_offline_artifact_removal(values, 250, _CHANNELS_10,
        ArtifactRemovalSpec(method=method, eog_channels=("Fp1",)))
    assert result.applied
    validity = result.to_evidence()["application_validity"]
    assert validity["additional_unavailable_channel_samples"] == 360
    assert validity["intervals"][0]["missing_input_channels"] == ["Fp1"]
    assert validity["intervals"][0]["additional_unavailable_channels"] == list(_CHANNELS_10[1:])

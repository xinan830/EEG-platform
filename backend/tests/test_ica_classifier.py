"""Unit tests for mne-icalabel ONNX classifier eligibility gates, offline inference, and conservative rules."""

from __future__ import annotations

import socket
import time
import warnings

import mne
import numpy as np
import pytest

from app.scientific import ica_classifier
from app.scientific.ica_classifier import (
    ICLABEL_CLASSES,
    IcaClassificationResult,
    IcaComponentClassification,
    classify_ica_components,
    compute_effective_rank,
    compute_ica_fit_bandpass_hz,
    evaluate_iclabel_recommendation,
    get_iclabel_runtime_metadata,
    merge_ica_scores_with_classification,
)


_STANDARD_19 = (
    "Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8",
    "T7", "C3", "Cz", "C4", "T8",
    "P7", "P3", "Pz", "P4", "P8", "O1", "O2",
)


def _car_provenance(channel_names: tuple[str, ...], *, suffix: str = "-AVG") -> dict[str, object]:
    raw_names = [ch.removesuffix(suffix) for ch in channel_names]
    return {
        "signal_mode": "montage_projected",
        "analysis_reference": "common_average",
        "formulas": [
            {
                "name": ch,
                "positive": raw,
                "negative_kind": "mean",
                "negative_labels": raw_names,
            }
            for ch, raw in zip(channel_names, raw_names, strict=True)
        ],
    }


def _ref_provenance(channel_names: tuple[str, ...]) -> dict[str, object]:
    return {
        "signal_mode": "montage_projected",
        "analysis_reference": "original_recording_no_software_rereference",
        "formulas": [
            {
                "name": ch,
                "positive": ch.split("-", 1)[0],
                "negative_kind": "OriginalHardwareReference",
                "negative_labels": [],
            }
            for ch in channel_names
        ],
    }


def _bipolar_provenance() -> dict[str, object]:
    pairs = (("Fp1-F7", "Fp1", "F7"), ("F7-T7", "F7", "T7"), ("Fp2-F8", "Fp2", "F8"), ("F8-T8", "F8", "T8"))
    return {
        "signal_mode": "montage_projected",
        "analysis_reference": "bipolar",
        "formulas": [
            {"name": name, "positive": pos, "negative_kind": "channel", "negative_labels": [neg]}
            for name, pos, neg in pairs
        ],
    }


def _fit_synthetic_car_ica(
    channel_names: tuple[str, ...] = tuple(f"{ch}-AVG" for ch in _STANDARD_19),
    *,
    sfreq: float = 250.0,
    duration_s: float = 16.0,
    n_components: int = 15,
    seed: int = 42,
):
    rng = np.random.default_rng(seed)
    n_samples = int(round(duration_s * sfreq))
    t = np.arange(n_samples, dtype=np.float64) / sfreq
    data = rng.normal(0.0, 8e-6, size=(len(channel_names), n_samples))
    # Inject alpha + frontal blink so ICA produces realistic physiological components
    alpha = 20e-6 * np.sin(2.0 * np.pi * 10.0 * t)
    blink = np.zeros(n_samples, dtype=np.float64)
    for center in (2.0, 5.0, 8.0, 11.0, 14.0):
        blink += 160e-6 * np.exp(-0.5 * ((t - center) / 0.08) ** 2)
    data[-2:, :] += alpha
    data[:2, :] += blink
    data -= data.mean(axis=0, keepdims=True)
    info = mne.create_info(list(channel_names), sfreq=sfreq, ch_types="eeg")
    raw = mne.io.RawArray(data, info, verbose=False)
    ica = mne.preprocessing.ICA(
        n_components=n_components,
        method="infomax",
        fit_params={"extended": True},
        random_state=seed,
        max_iter=150,
        verbose=False,
    )
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        ica.fit(raw, verbose=False)
    return raw, ica, channel_names


def test_iclabel_ready_on_car_with_3d_coords_offline_and_fast(monkeypatch):
    raw, ica, names = _fit_synthetic_car_ica()

    def _forbid_network(*args, **kwargs):
        raise AssertionError("ICLabel must not access network during offline inference")

    monkeypatch.setattr(socket, "socket", _forbid_network)

    started = time.perf_counter()
    res = classify_ica_components(raw, ica, names, 250.0, signal_provenance=_car_provenance(names))
    elapsed = time.perf_counter() - started

    assert res.classifier_status == "ready"
    assert res.classifier_method == "mne_icalabel_onnx"
    assert res.classifier_unavailable_reason is None
    assert res.classifier_version and "onnx" in res.classifier_version
    assert elapsed < 3.0  # comfortably bounded even on cold CI; warm CPU is ~0.4s
    assert len(res.components) == 15
    for item in res.components:
        assert item.iclabel_label in ICLABEL_CLASSES
        assert set(item.iclabel_probabilities.keys()) == set(ICLABEL_CLASSES)
        assert abs(sum(item.iclabel_probabilities.values()) - 1.0) < 0.01
        assert abs(item.iclabel_probability - item.iclabel_probabilities[item.iclabel_label]) < 1e-4


def test_iclabel_falls_back_on_ref_and_bipolar_without_running_model(monkeypatch):
    raw, ica, names = _fit_synthetic_car_ica(n_components=6)

    def _should_not_run(*args, **kwargs):
        raise AssertionError("ICLabel ONNX model must not execute on non-CAR montage")

    fake_meta = {
        "mne_icalabel_version": "test",
        "onnxruntime_version": "test",
        "iclabel_model_sha256": "0123456789abcdef",
        "recommendation_rule_version": "iclabel-rec-v2-brain-guard",
        "classifier_version": "mne-icalabel-test-onnx",
    }
    monkeypatch.setattr(ica_classifier, "_load_iclabel_runtime", lambda: (_should_not_run, fake_meta))

    ref_res = classify_ica_components(raw, ica, names, 250.0, signal_provenance=_ref_provenance(names))
    assert ref_res.classifier_status == "fallback_eog_ecg"
    assert ref_res.classifier_method == "eog_ecg_correlation"
    assert ref_res.classifier_unavailable_reason == "non_average_reference_montage"
    assert ref_res.components == ()

    none_res = classify_ica_components(raw, ica, names, 250.0, signal_provenance=None)
    assert none_res.classifier_status == "fallback_eog_ecg"
    assert none_res.classifier_unavailable_reason == "non_average_reference_montage"

    bipolar_names = ("Fp1-F7", "F7-T7", "Fp2-F8", "F8-T8")
    raw_bp, ica_bp, _ = _fit_synthetic_car_ica(bipolar_names, n_components=3)
    bp_res = classify_ica_components(raw_bp, ica_bp, bipolar_names, 250.0, signal_provenance=_bipolar_provenance())
    assert bp_res.classifier_status == "fallback_eog_ecg"
    assert bp_res.classifier_unavailable_reason == "non_average_reference_montage"


def test_iclabel_falls_back_on_missing_or_duplicate_3d_coords_and_low_sfreq():
    bad_names = ("Fp1-AVG", "Fp2-AVG", "C3-AVG", "CUSTOM99-AVG")
    raw, ica, _ = _fit_synthetic_car_ica(bad_names, n_components=3)
    res_missing = classify_ica_components(raw, ica, bad_names, 250.0, signal_provenance=_car_provenance(bad_names))
    assert res_missing.classifier_status == "fallback_eog_ecg"
    assert res_missing.classifier_unavailable_reason == "electrode_3d_coordinates_incomplete"

    # T3 and T7 map to the exact same 3D coordinate in colin27_1020 -> must reject duplicates
    dup_names = ("Fp1-AVG", "Fp2-AVG", "T3-AVG", "T7-AVG")
    raw_dup, ica_dup, _ = _fit_synthetic_car_ica(dup_names, n_components=3)
    res_dup = classify_ica_components(raw_dup, ica_dup, dup_names, 250.0, signal_provenance=_car_provenance(dup_names))
    assert res_dup.classifier_status == "fallback_eog_ecg"
    assert res_dup.classifier_unavailable_reason == "electrode_3d_coordinates_incomplete"

    # Low sampling rate (< 104 Hz)
    low_names = ("Fp1-AVG", "Fp2-AVG", "C3-AVG", "O1-AVG")
    raw_low, ica_low, _ = _fit_synthetic_car_ica(low_names, sfreq=100.0, n_components=3)
    res_low = classify_ica_components(raw_low, ica_low, low_names, 100.0, signal_provenance=_car_provenance(low_names))
    assert res_low.classifier_status == "fallback_eog_ecg"
    assert res_low.classifier_unavailable_reason == "sampling_rate_too_low_for_icalabel"


def test_iclabel_reports_unavailable_when_dependency_missing(monkeypatch):
    raw, ica, names = _fit_synthetic_car_ica(n_components=4)
    empty_meta = {
        "mne_icalabel_version": None,
        "onnxruntime_version": None,
        "iclabel_model_sha256": None,
        "recommendation_rule_version": "iclabel-rec-v2-brain-guard",
        "classifier_version": None,
    }
    monkeypatch.setattr(ica_classifier, "_load_iclabel_runtime", lambda: (None, empty_meta))
    res = classify_ica_components(raw, ica, names, 250.0, signal_provenance=_car_provenance(names))
    assert res.classifier_status == "unavailable"
    assert res.classifier_method == "eog_ecg_correlation"
    assert res.classifier_unavailable_reason == "icalabel_dependency_unavailable"


def test_conservative_recommendation_rules_never_recommend_brain_or_other():
    # High-confidence artifact with low brain prob -> recommended
    for art_class in ("eye blink", "muscle artifact", "heart beat", "line noise", "channel noise"):
        probs = {cls: 0.01 for cls in ICLABEL_CLASSES}
        probs["brain"] = 0.05
        probs[art_class] = 0.89
        rec, reasons = evaluate_iclabel_recommendation(probs)
        assert rec is True
        assert reasons == (f"iclabel:{art_class.replace(' ', '_')}:0.89",)

    # Mixed brain/artifact (brain >= 0.25) -> NOT recommended
    rec_mixed, reasons_mixed = evaluate_iclabel_recommendation(
        {"brain": 0.25, "eye blink": 0.75, "muscle artifact": 0.0, "heart beat": 0.0, "line noise": 0.0, "channel noise": 0.0, "other": 0.0}
    )
    assert rec_mixed is False and reasons_mixed == ()

    # Below 0.75 threshold -> NOT recommended
    rec_low, _ = evaluate_iclabel_recommendation(
        {"brain": 0.10, "eye blink": 0.74, "muscle artifact": 0.06, "heart beat": 0.02, "line noise": 0.02, "channel noise": 0.02, "other": 0.04}
    )
    assert rec_low is False

    # brain or other -> NEVER recommended even at 0.99
    for safe_class in ("brain", "other"):
        probs = {cls: 0.001 for cls in ICLABEL_CLASSES}
        probs[safe_class] = 0.994
        rec_safe, reasons_safe = evaluate_iclabel_recommendation(probs)
        assert rec_safe is False and reasons_safe == ()


def test_broadband_fit_bandpass_adapts_to_nyquist():
    assert compute_ica_fit_bandpass_hz(250.0) == (1.0, 100.0, False)
    assert compute_ica_fit_bandpass_hz(500.0) == (1.0, 100.0, False)
    assert compute_ica_fit_bandpass_hz(128.0) == (1.0, 62.0, True)
    assert compute_ica_fit_bandpass_hz(160.0) == (1.0, 78.0, True)
    assert compute_ica_fit_bandpass_hz(100.0, main_low_hz=1.0, main_high_hz=45.0) == (1.0, 45.0, True)


def test_effective_rank_detects_interpolation_and_car_rank_deficiency():
    ch21 = ("Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8", "T7", "C3", "Cz", "C4", "T8", "M1", "P7", "P3", "Pz", "P4", "P8", "M2", "O1", "O2")
    rng = np.random.default_rng(19)
    raw = mne.io.RawArray(rng.normal(0.0, 15e-6, size=(len(ch21), 250 * 10)), mne.create_info(list(ch21), 250.0, "eeg"), verbose=False)
    raw.set_montage(mne.channels.make_standard_montage("colin27_1020"))
    raw.info["bads"] = ["F3", "P4"]
    raw.interpolate_bads(reset_bads=True, verbose=False)
    raw.set_eeg_reference("average", verbose=False)
    assert compute_effective_rank(raw.get_data().T) == 18


def test_nyquist_limited_and_non_converged_states_disable_auto_recommendation():
    raw_128, ica_128, names = _fit_synthetic_car_ica(sfreq=128.0, n_components=8)
    res_limited = classify_ica_components(
        raw_128, ica_128, names, 128.0,
        signal_provenance=_car_provenance(names),
        fit_band_limited_by_nyquist=True,
        ica_converged=True,
    )
    assert res_limited.classifier_status == "ready_limited"
    assert res_limited.classifier_unavailable_reason == "fit_band_limited_by_nyquist"
    assert len(res_limited.components) == 8
    assert all(not item.recommended_exclude for item in res_limited.components)

    raw_250, ica_250, names_250 = _fit_synthetic_car_ica(sfreq=250.0, n_components=6)
    res_unconv = classify_ica_components(
        raw_250, ica_250, names_250, 250.0,
        signal_provenance=_car_provenance(names_250),
        fit_band_limited_by_nyquist=False,
        ica_converged=False,
    )
    assert res_unconv.classifier_status == "advisory_only"
    assert res_unconv.classifier_unavailable_reason == "ica_not_converged"
    assert all(not item.recommended_exclude for item in res_unconv.components)


def test_brain_and_other_guard_overrides_dedicated_eog_ecg_auto_exclusion():
    brain_probs = {cls: 0.01 for cls in ICLABEL_CLASSES}
    brain_probs["brain"] = 0.94
    other_probs = {cls: 0.02 for cls in ICLABEL_CLASSES}
    other_probs["other"] = 0.88
    eye_probs = {cls: 0.01 for cls in ICLABEL_CLASSES}
    eye_probs["eye blink"] = 0.94

    cls_res = IcaClassificationResult(
        classifier_method="mne_icalabel_onnx",
        classifier_status="ready",
        components=(
            IcaComponentClassification(0, "brain", 0.94, brain_probs, False, ()),
            IcaComponentClassification(1, "other", 0.88, other_probs, False, ()),
            IcaComponentClassification(2, "eye blink", 0.94, eye_probs, True, ("iclabel:eye_blink:0.94",)),
        ),
    )
    rec, rows = merge_ica_scores_with_classification(
        n_components=3,
        eog_scores=np.array([0.89, 0.85, 0.92]),
        eog_names=("EOG1",),
        has_dedicated_eog=True,
        auto_eog={0, 1, 2},
        ecg_scores=np.array([0.82, 0.10, 0.05]),
        ecg_names=("ECG1",),
        auto_ecg={0},
        explained_variance_ratios=[0.4, 0.2, 0.2],
        classification=cls_res,
        ica_converged=True,
    )
    # IC0 (brain) and IC1 (other) MUST NEVER be auto-recommended even with high dedicated EOG/ECG correlation
    assert rec == (2,)
    assert rows[0]["recommended_exclude"] is False
    assert any("eog_correlation_conflicts_with_brain" in item for item in rows[0]["advisory_conflicts"])
    assert any("ecg_correlation_conflicts_with_brain" in item for item in rows[0]["advisory_conflicts"])
    assert rows[1]["recommended_exclude"] is False
    assert any("eog_correlation_conflicts_with_other" in item for item in rows[1]["advisory_conflicts"])
    assert rows[2]["recommended_exclude"] is True


def test_runtime_metadata_exposes_auditable_onnx_hash_and_versions():
    meta = get_iclabel_runtime_metadata()
    assert meta["mne_icalabel_version"]
    assert meta["onnxruntime_version"]
    assert meta["iclabel_model_sha256"] and len(meta["iclabel_model_sha256"]) == 16
    assert meta["recommendation_rule_version"] == "iclabel-rec-v2-brain-guard"

import json
import hashlib
from pathlib import Path

import numpy as np
import pytest

from app.scientific.contracts import (
    AlgorithmQualityContract,
    GapEvidence,
    OutputField,
    OutputSchema,
    PreprocessingSnapshot,
    QualityEvidence,
    SampleRange,
    TransformPaddingEvidence,
)
from app.scientific.primitives.spectral import band_power, estimate_spectrogram, estimate_welch_psd, preprocess_offline
from app.algorithms.faa.official import compute_faa
from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.rbp.official import RBP_BANDS
from app.algorithms.theta_beta.official import metric_values
from app.scientific.primitives.spectral import SpectralEstimate


def test_sample_range_is_recording_relative_half_open_and_derives_seconds():
    sample_range = SampleRange(100, 350)
    assert sample_range.length == 250
    assert sample_range.seconds(100.0) == (1.0, 3.5)


def test_sample_range_has_one_authoritative_seconds_conversion_rule():
    sample_range = SampleRange.from_seconds(1.001, 3.499, 100.0)
    assert sample_range.as_dict() == {"start_sample": 100, "end_sample": 350, "half_open": True}
    assert sample_range.seconds(100.0) == (1.0, 3.5)
    with pytest.raises(ValueError):
        SampleRange.from_seconds(1.0, 1.0, 100.0)


def test_quality_evidence_requires_reason_for_unavailable_results():
    with pytest.raises(ValueError):
        QualityEvidence("unavailable")
    evidence = QualityEvidence("unavailable", ("gap",))
    assert evidence.reasons == ("gap",)


def test_algorithm_quality_contract_declares_versioned_rules_and_layers():
    contract = AlgorithmQualityContract(
        algorithm_id="iapf",
        version="iapf-quality-v1",
        rule_ids=("finite_input", "alpha_peak_gate"),
    )
    assert contract.as_dict() == {
        "algorithm_id": "iapf",
        "version": "iapf-quality-v1",
        "required_layers": ["DataIntegrity", "SignalQuality", "AlgorithmValidity"],
        "rule_ids": ["finite_input", "alpha_peak_gate"],
    }
    with pytest.raises(ValueError):
        AlgorithmQualityContract("iapf", "v1", required_layers=("SignalQuality", "SignalQuality"))


def test_output_schema_requires_unique_unit_bearing_fields():
    schema = OutputSchema((OutputField("power", "V^2", "band power"),))
    assert schema.fields[0].unit == "V^2"
    with pytest.raises(ValueError):
        OutputSchema((OutputField("power", "V^2", "a"), OutputField("power", "V^2", "b")))


def test_preprocessing_snapshot_keeps_gap_policy_and_si_unit_explicit():
    snapshot = PreprocessingSnapshot(reference="original", filters=("bandpass:1-30Hz",))
    assert snapshot.gap_policy == "reject"
    assert snapshot.unit == "V"


def test_gap_and_transform_padding_evidence_are_distinct_contracts():
    gap = GapEvidence(True, "reject", non_finite_samples=2)
    assert gap.as_dict() == {
        "detected": True,
        "policy": "reject",
        "missing_samples": 0,
        "non_finite_samples": 2,
        "imputed": False,
    }
    padding = TransformPaddingEvidence(True, "fft_boundary", samples=4)
    assert padding.as_dict() == {"used": True, "kind": "fft_boundary", "samples": 4}
    with pytest.raises(ValueError):
        GapEvidence(False, "reject", non_finite_samples=1)
    with pytest.raises(ValueError):
        TransformPaddingEvidence(False, "fft_boundary", samples=4)


def test_migrated_spectral_authority_matches_frozen_synthetic_baseline():
    fixture = json.loads((Path(__file__).parent / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    goldens = fixture["offline_spectral"]["synthetic_10hz_6hz_goldens"]
    sfreq_hz = 100.0
    times = np.arange(40 * int(sfreq_hz), dtype=float) / sfreq_hz
    source = np.column_stack((
        10e-6 * np.sin(2.0 * np.pi * 10.0 * times),
        8e-6 * np.sin(2.0 * np.pi * 6.0 * times),
    ))

    filtered = preprocess_offline(source, sfreq_hz)[: int(30 * sfreq_hz)]
    spectrum = estimate_welch_psd(filtered, sfreq_hz)
    alpha_f3 = float(band_power(spectrum.freqs, spectrum.psd[0], 8.0, 13.0) * 1e12)
    theta_fz = float(band_power(spectrum.freqs, spectrum.psd[1], 4.0, 8.0) * 1e12)
    band_values = [
        float(band_power(spectrum.freqs, spectrum.psd[0], low, high) * 1e12)
        for low, high in ((1.0, 4.0), (4.0, 8.0), (8.0, 13.0), (13.0, 30.0))
    ]
    alpha_relative = alpha_f3 / sum(band_values)
    rules = fixture["comparison_rules"]["floating_point"]
    np.testing.assert_allclose(alpha_f3, goldens["F3_alpha_power_uV2"], rtol=rules["default_rtol"], atol=1e-12)
    np.testing.assert_allclose(theta_fz, goldens["Fz_theta_power_uV2"], rtol=rules["default_rtol"], atol=1e-12)
    np.testing.assert_allclose(alpha_relative, goldens["F3_alpha_relative_power"], rtol=rules["default_rtol"], atol=1e-12)


def test_migrated_array_axes_and_artifacts_match_frozen_checksums():
    fixture = json.loads((Path(__file__).parent / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    sfreq_hz = 100.0
    times = np.arange(40 * int(sfreq_hz), dtype=float) / sfreq_hz
    source = np.column_stack((
        10e-6 * np.sin(2.0 * np.pi * 10.0 * times),
        8e-6 * np.sin(2.0 * np.pi * 6.0 * times),
    ))

    def checksum(values: np.ndarray) -> str:
        return hashlib.sha256(np.ascontiguousarray(values, dtype=np.float64).tobytes()).hexdigest()

    spectral = fixture["offline_spectral"]["array_baseline"]
    estimate = estimate_welch_psd(preprocess_offline(source, sfreq_hz)[:3000], sfreq_hz)
    assert list(estimate.freqs.shape) == spectral["frequency_shape"]
    assert checksum(estimate.freqs) == spectral["frequency_sha256"]
    assert list(estimate.psd.shape) == spectral["psd_shape"]
    assert checksum(estimate.psd) == spectral["psd_sha256"]

    stft = fixture["offline_spectral"]["stft_array_baseline"]
    centers, frequencies, power = estimate_spectrogram(source[:600], sfreq_hz)
    assert list(centers.shape) == stft["centers_shape"]
    assert checksum(centers) == stft["centers_sha256"]
    assert list(frequencies.shape) == stft["frequency_shape"]
    assert checksum(frequencies) == stft["frequency_sha256"]
    assert list(power.shape) == stft["power_shape"]
    assert checksum(power) == stft["power_sha256"]


def test_baseline_comparison_rejects_coordinate_shift_and_tolerance_violation():
    expected = np.asarray([1.0, 2.0, 3.0])
    shifted = expected.copy()
    shifted[1] += 0.001
    with pytest.raises(AssertionError):
        np.testing.assert_array_equal(shifted, expected)

    changed = expected.copy()
    changed[2] += 1e-4
    with pytest.raises(AssertionError):
        np.testing.assert_allclose(changed, expected, rtol=1e-9, atol=1e-12)


def test_migrated_official_algorithms_match_frozen_synthetic_baseline():
    fixture = json.loads((Path(__file__).parent / "fixtures" / "algorithm_runtime_baseline.json").read_text(encoding="utf-8"))
    rules = fixture["comparison_rules"]["floating_point"]
    frequencies = np.arange(1.0, 30.25, 0.25)
    baseline = 1e-12 / frequencies
    peak = baseline + 8e-12 * np.exp(-0.5 * ((frequencies - 10.0) / 0.5) ** 2)
    spectrum = SpectralEstimate(frequencies, np.vstack((peak, peak, peak)), 1.0, 14, 14, None)

    iapf = estimate_iapf(spectrum)
    assert iapf.source == "peak"
    np.testing.assert_allclose(iapf.value, fixture["iapf"]["synthetic_peak_hz"], rtol=rules["default_rtol"], atol=rules["default_atol"])

    powers = [float(band_power(frequencies, peak, low, high)) for _, low, high in RBP_BANDS]
    rbp = np.asarray(powers) / sum(powers)
    np.testing.assert_allclose(rbp, fixture["rbp"]["relative_band_power"], rtol=rules["default_rtol"], atol=rules["default_atol"])

    times = np.arange(int(30 * 100), dtype=float) / 100.0
    faa = compute_faa(10e-6 * np.sin(2.0 * np.pi * 10.0 * times), 20e-6 * np.sin(2.0 * np.pi * 10.0 * times), 100.0)
    np.testing.assert_allclose(faa["faa"], fixture["faa"]["synthetic_faa"], rtol=rules["default_rtol"], atol=rules["default_atol"])

    theta_beta = metric_values(spectrum, 10.0)
    np.testing.assert_allclose(theta_beta["brainbeat"], fixture["theta_beta_v2"]["synthetic_brainbeat"], rtol=rules["default_rtol"], atol=rules["default_atol"])
    np.testing.assert_allclose(theta_beta["fatigue"]["Fz"], fixture["theta_beta_v2"]["synthetic_fatigue_fz"], rtol=rules["default_rtol"], atol=rules["default_atol"])

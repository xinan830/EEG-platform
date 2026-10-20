"""Boundary-condition tests for the nine official algorithm modules (task 3.1).

These tests probe module-level contracts only: parameter order, missing or
duplicate channels, sampling-rate/Nyquist limits, frequency coverage, short
ranges, and invalid numeric inputs. They do not change any scientific
contract and do not replace independent numerical references (task 4.x).
"""

from __future__ import annotations

import numpy as np
import pytest

from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.algorithms.band_ratio.compute import compute_band_ratio
from app.algorithms.faa.official import compute_faa
from app.algorithms.brainbeat.compute import compute_brainbeat
from app.algorithms.brainbeat.runner import BrainbeatAlgorithm
from app.algorithms.brainbeat.config import BrainbeatConfig
from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.peak_frequency.compute import compute_peak_frequency
from app.algorithms.rbp.official import five_band_shares
from app.algorithms.rbp.runner import RbpAlgorithm
from app.algorithms.rbp.manifest import MANIFEST as RBP_MANIFEST
from app.algorithms.stft.runner import StftAlgorithm
from app.algorithms.stft.config import StftConfig
from app.algorithm_runtime.contracts import AlgorithmInputs
from app.algorithms.faa.runner import FaaAlgorithm
from app.algorithms.faa.config import FaaConfig
from app.algorithms.iapf.compute import compute_iapf
from app.algorithms.iapf.runner import IapfAlgorithm
from app.algorithms.theta_beta.compute import compute_theta_beta
from app.algorithms.theta_beta.official import metric_values
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm
from app.eeg_core.official_algorithms.brainbeat import segment_brainbeat
from app.scientific.primitives.spectral import (
    SpectralEstimate,
    band_power,
    estimate_welch_psd,
    preprocess_offline,
    estimate_spectrogram_with_quality,
)


def _spectrum(freqs, psd, gate_failed=None, sfreq=250.0) -> SpectralEstimate:
    return SpectralEstimate(
        np.asarray(freqs, dtype=float),
        np.atleast_2d(np.asarray(psd, dtype=float)),
        1.0, 4, 4, gate_failed,
        evidence={"sfreq_hz": sfreq},
    )


def _flat_spectrum(freq_low=1.0, freq_high=30.0, step=0.25, channels=3, **kwargs):
    freqs = np.arange(freq_low, freq_high + step / 2, step)
    psd = np.full((channels, len(freqs)), 1e-12)
    return _spectrum(freqs, psd, **kwargs)


REQUESTED = {"start_s": 0.0, "end_s": 10.0}


class TestPeakFrequencyBoundaries:
    def test_band_not_covered_fails_structured(self):
        result = compute_peak_frequency(
            _flat_spectrum(1.0, 20.0), channel="Fz", low_hz=1.0, high_hz=30.0,
            requested_range=REQUESTED,
        )
        assert result.value is None
        assert result.failure.code == "PEAK_FREQUENCY_BAND_NOT_COVERED"

    def test_no_bin_inside_band_fails(self):
        result = compute_peak_frequency(
            _flat_spectrum(1.0, 30.0, step=0.25), channel="Fz",
            low_hz=5.1, high_hz=5.2, requested_range=REQUESTED,
        )
        assert result.failure.code == "PEAK_FREQUENCY_NO_BIN"

    def test_tie_prefers_lowest_frequency(self):
        freqs = np.array([8.0, 10.0, 12.0, 14.0])
        psd = np.array([[1.0, 2.0, 2.0, 1.0]])
        result = compute_peak_frequency(
            _spectrum(freqs, psd), channel="Fz", low_hz=8.0, high_hz=14.0,
            requested_range=REQUESTED,
        )
        assert result.value == 10.0

    def test_band_edges_are_inclusive(self):
        freqs = np.array([7.0, 8.0, 10.0, 12.0, 13.0, 14.0])
        psd = np.array([[1.0, 9.0, 2.0, 3.0, 10.0, 1.0]])
        result = compute_peak_frequency(
            _spectrum(freqs, psd), channel="Fz", low_hz=8.0, high_hz=13.0,
            requested_range=REQUESTED,
        )
        assert result.value == 13.0

    def test_nonfinite_band_values_fail(self):
        freqs = np.array([8.0, 10.0, 12.0])
        psd = np.array([[1.0, np.nan, 3.0]])
        result = compute_peak_frequency(
            _spectrum(freqs, psd), channel="Fz", low_hz=8.0, high_hz=12.0,
            requested_range=REQUESTED,
        )
        assert result.failure.code == "PEAK_FREQUENCY_NONFINITE"

    def test_gate_failed_short_circuits(self):
        result = compute_peak_frequency(
            _flat_spectrum(gate_failed="low_quality"), channel="Fz",
            low_hz=1.0, high_hz=30.0, requested_range=REQUESTED,
        )
        assert result.quality == "gate_failed"
        assert result.failure.code == "PSD_QUALITY_GATE_FAILED"


class TestBandRatioBoundaries:
    def test_zero_denominator_fails(self):
        freqs = np.array([1.0, 2.0, 3.0, 4.0])
        psd = np.array([[1e-12, 0.0, 0.0, 1e-12]])
        result = compute_band_ratio(
            _spectrum(freqs, psd), channel="Fz",
            numerator_low_hz=1.0, numerator_high_hz=2.0,
            denominator_low_hz=2.0, denominator_high_hz=3.0,
            requested_range=REQUESTED,
        )
        assert result.failure.code == "BAND_RATIO_DENOMINATOR_INVALID"

    def test_nonfinite_psd_fails(self):
        freqs = np.array([1.0, 2.0, 3.0])
        psd = np.array([[1e-12, np.inf, 1e-12]])
        result = compute_band_ratio(
            _spectrum(freqs, psd), channel="Fz",
            numerator_low_hz=1.0, numerator_high_hz=2.0,
            denominator_low_hz=2.0, denominator_high_hz=3.0,
            requested_range=REQUESTED,
        )
        assert result.value is None
        assert result.quality == "unavailable"

    def test_integral_uses_linear_interpolated_band_edges(self):
        freqs = np.array([0.0, 1.0, 2.0, 3.0])
        psd = np.array([[0.0, 1.0, 2.0, 3.0]])
        result = compute_band_ratio(
            _spectrum(freqs, psd), channel="Fz",
            numerator_low_hz=0.5, numerator_high_hz=1.5,
            denominator_low_hz=1.5, denominator_high_hz=2.5,
            requested_range=REQUESTED,
        )
        assert result.value == pytest.approx(1.0 / 2.0)
        trace = result.evidence["extensions"]["band_ratio_evidence"]
        assert trace["integration"] == "linear_edge_interpolation_trapezoid"

    def test_nonfinite_denominator_power_returns_null(self):
        freqs = np.array([0.0, 1.0, 2.0, 3.0])
        psd = np.array([[1.0, 1.0, np.inf, 1.0]])
        result = compute_band_ratio(
            _spectrum(freqs, psd), channel="Fz",
            numerator_low_hz=0.0, numerator_high_hz=1.0,
            denominator_low_hz=1.0, denominator_high_hz=2.0,
            requested_range=REQUESTED,
        )
        assert result.value is None
        assert result.failure.code == "BAND_RATIO_DENOMINATOR_INVALID"


class TestRbpBoundaries:
    def test_partial_nyquist_coverage_rejected(self):
        with pytest.raises(ValueError, match="1–50"):
            five_band_shares(_flat_spectrum(1.0, 40.0, channels=1))

    def test_nonfinite_density_rejected(self):
        freqs = np.arange(1.0, 50.25, 0.25)
        psd = np.full((1, len(freqs)), 1e-12)
        psd[0, 10] = np.nan
        with pytest.raises(ValueError):
            five_band_shares(_spectrum(freqs, psd))

    def test_shares_sum_to_one_on_full_coverage(self):
        powers, shares = five_band_shares(_flat_spectrum(1.0, 50.0, channels=1))
        assert sum(shares.values()) == pytest.approx(1.0, abs=1e-12)
        assert set(powers) == {"delta", "theta", "alpha", "beta", "gamma"}

    def test_gamma_has_nonzero_power_and_current_identity_is_v2(self):
        _, shares = five_band_shares(_flat_spectrum(1.0, 50.0, channels=1))
        assert shares["gamma"] > 0
        assert RBP_MANIFEST.scientific_version == "official-rbp-v2"
        assert "gamma" in RBP_MANIFEST.output_schema["fields"][-1]["name"].lower() or len(RBP_MANIFEST.output_schema["fields"]) == 5

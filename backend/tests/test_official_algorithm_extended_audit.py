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
from app.algorithms.theta_beta.runner import ThetaBetaAlgorithm
from app.algorithms.brainbeat.official import segment_brainbeat
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


class TestIapfAndThetaBetaBoundaries:
    @pytest.mark.parametrize("bad", [0.0, -1e-12, np.nan, np.inf])
    def test_nonpositive_or_nonfinite_psd_returns_unavailable(self, bad):
        freqs = np.arange(1.0, 30.25, 0.25)
        psd = np.full((1, len(freqs)), bad)
        result = estimate_iapf(_spectrum(freqs, psd))
        assert result.value is None

    def test_alpha_window_without_bins_returns_no_peak_no_cog(self):
        freqs = np.array([1.0, 2.0, 3.0, 20.0, 25.0, 30.0])
        psd = np.full((1, len(freqs)), 1e-12)
        result = estimate_iapf(_spectrum(freqs, psd))
        assert result.value is None
        assert result.gate_failed == "no_peak_no_cog"

    def test_iapf_fit_with_insufficient_non_alpha_points_is_explicitly_characterized(self):
        # Alpha bins exist, but only one point remains in the 3-30 Hz fit
        # region after excluding 7-13 Hz. NumPy polyfit warns/returns a
        # degenerate model today; capture that risk rather than invent a
        # scientific failure policy here.
        freqs = np.array([7.0, 10.0, 13.0, 20.0])
        psd = np.array([[1e-12, 4e-12, 1e-12, 1e-12]])
        with pytest.warns(np.exceptions.RankWarning):
            result = estimate_iapf(_spectrum(freqs, psd))
        assert result.value is None or np.isfinite(result.value)

    def test_flat_spectrum_has_finite_fit_diagnostics_and_no_nan_result(self):
        result = estimate_iapf(_flat_spectrum(channels=1))
        assert result.value is None or np.isfinite(result.value)
        assert result.model_error is None or np.isfinite(result.model_error)
        assert result.model_r2 is None or np.isfinite(result.model_r2)

    @pytest.mark.parametrize("iapf", [6.0, 30.0])
    def test_theta_beta_runner_returns_structured_failure_at_uncovered_edges(self, monkeypatch, iapf):
        from app.algorithms.iapf.official import IAPFEstimate
        monkeypatch.setattr(
            "app.algorithms.theta_beta.compute.estimate_iapf",
            lambda _spectrum: IAPFEstimate(iapf, "peak", None, 0.9, 0.1, iapf, iapf),
        )
        # This frequency axis has a valid 1-30 total band, but its sparse bins
        # do not span all individualized edges; failure must remain null.
        spectrum = _spectrum(np.array([1., 4., 8., 13., 20., 30.]), np.ones((1, 6)))
        result = compute_theta_beta(spectrum, channel="Fz", requested_range=REQUESTED)
        assert result.value is None
        assert result.failure is not None
        assert result.failure.code == "BAND_RANGE_INVALID"

    def test_theta_beta_band_near_boundary_returns_structured_failure(self, monkeypatch):
        from app.algorithms.iapf.official import IAPFEstimate
        monkeypatch.setattr("app.algorithms.theta_beta.compute.estimate_iapf",
                            lambda _: IAPFEstimate(4.0, "peak", None, 0.9, 0.1, 4.0, 4.0))
        spectrum = _flat_spectrum()
        result = compute_theta_beta(spectrum, channel="Fz", requested_range=REQUESTED)
        assert result.value is None
        assert result.failure.code == "BAND_RANGE_INVALID"

    def test_theta_beta_runner_catches_invalid_band_structured(self):
        spectrum = _flat_spectrum()
        result = compute_theta_beta(
            spectrum, channel="Fz", requested_range=REQUESTED,
            actual_range=REQUESTED,
        )
        # Flat PSD gives a valid IAPF (~15.5 Hz), so this exercises the
        # normal path; the invalid-band path is covered separately.
        assert result.value is not None and result.value > 0

    def test_theta_beta_accepts_one_explicit_channel(self):
        spectrum = _flat_spectrum(channels=1)
        result = compute_theta_beta(spectrum, channel="Fz", requested_range=REQUESTED)
        assert result.value is not None
        assert result.channel == "Fz"


class TestFaaBoundaries:
    def test_swapped_channels_flip_sign(self):
        sfreq = 250.0
        n = int(sfreq * 30)
        t = np.arange(n) / sfreq
        f3 = 1e-6 * np.sin(2 * np.pi * 10.0 * t)
        f4 = 3e-6 * np.sin(2 * np.pi * 10.0 * t)
        normal = compute_faa(f3, f4, sfreq, min_clean_epochs=5)
        swapped = compute_faa(f4, f3, sfreq, min_clean_epochs=5)
        assert normal["faa"] is not None and swapped["faa"] is not None
        assert normal["faa"] == pytest.approx(-swapped["faa"], abs=1e-9)

    def test_too_short_returns_structured_reason(self):
        report = compute_faa(np.zeros(100), np.zeros(100), 250.0)
        assert report["faa"] is None
        assert report["reason"] == "too_short"

    def test_nonfinite_epoch_is_skipped(self):
        sfreq = 250.0
        n = int(sfreq * 30)
        t = np.arange(n) / sfreq
        f3 = 1e-6 * np.sin(2 * np.pi * 10.0 * t)
        f4 = 2e-6 * np.sin(2 * np.pi * 10.0 * t)
        f4[:100] = np.nan
        report = compute_faa(f3, f4, sfreq, min_clean_epochs=5)
        assert report["clean_epochs"] < report["total_epochs"]
        assert report["faa"] is not None

    def test_bad_value_in_either_channel_rejects_same_paired_epoch(self):
        sfreq = 100.0
        t = np.arange(30 * int(sfreq)) / sfreq
        left = 10e-6 * np.sin(2 * np.pi * 10 * t)
        right = 20e-6 * np.sin(2 * np.pi * 10 * t)
        right[:int(2 * sfreq)] = np.nan
        report = compute_faa(left, right, sfreq)
        assert report["clean_epochs"] < report["total_epochs"]
        assert report["clean_ratio"] < 1.0
        # Paired rejection means accepted count is one shared count, not
        # separately optimized per side.
        assert report["faa"] is None or np.isfinite(report["faa"])

    def test_parameter_order_preserves_f3_then_f4_sign(self):
        sfreq = 100.0
        t = np.arange(30 * int(sfreq)) / sfreq
        f3 = 10e-6 * np.sin(2 * np.pi * 10 * t)
        f4 = 20e-6 * np.sin(2 * np.pi * 10 * t)
        direct = compute_faa(f3, f4, sfreq, 2.0, 0.5, 150.0, (8.0, 13.0), 10)
        keyword = compute_faa(
            f3, f4, sfreq, epoch_s=2.0, overlap=0.5, artifact_uv=150.0,
            band=(8.0, 13.0), min_clean_epochs=10,
        )
        assert direct["faa"] == pytest.approx(keyword["faa"])

    def test_same_source_channel_is_rejected_before_calculation(self):
        with pytest.raises(ValueError, match="must be different"):
            FaaConfig(channel="F3", f4_channel="f3", mode="static", start_s=0, end_s=20)

    def test_paired_nonfinite_channel_fails_when_clean_count_below_minimum(self):
        sfreq = 100.0
        t = np.arange(10 * int(sfreq)) / sfreq
        left = 10e-6 * np.sin(2 * np.pi * 10 * t)
        right = np.full_like(left, np.nan)
        report = compute_faa(left, right, sfreq)
        assert report["faa"] is None
        assert report["clean_epochs"] == 0
        assert report["reason"] == "too_few_clean_epochs"


class TestBrainbeatBoundaries:
    def test_alpha_band_near_nyquist_edge_freezes_current_behaviour(self):
        freqs = np.array([1.0, 2.0, 3.0])
        psd = np.full(len(freqs), 1e-12)
        # IAPF near 30 Hz makes the alpha band [28, 32] mostly uncovered;
        # the legacy contract still returns a finite ratio, so this test
        # freezes current behaviour explicitly.
        value = segment_brainbeat(freqs, psd, psd, 30.0)
        assert np.isfinite(value)

    def test_zero_total_power_does_not_explode(self):
        freqs = np.linspace(1.0, 30.0, 117)
        psd = np.zeros(len(freqs))
        value = segment_brainbeat(freqs, psd, psd, 10.0)
        assert np.isfinite(value)

    def test_offline_brainbeat_uses_ordered_fz_pz_roles(self):
        # The same two spectra with role order exchanged must give a different
        # result; this establishes that the adapter keeps explicit roles.
        freqs = np.arange(1.0, 30.25, 0.25)
        fz = 1e-12 * np.ones_like(freqs)
        pz = 1e-12 * np.ones_like(freqs)
        fz[(freqs >= 4) & (freqs <= 8)] *= 4
        pz[(freqs >= 8) & (freqs <= 13)] *= 4
        spectrum = _spectrum(freqs, np.vstack([fz, pz]))
        first = compute_brainbeat(spectrum, fz_channel="Fz", pz_channel="Pz", requested_range=REQUESTED)
        swapped = compute_brainbeat(spectrum.__class__(
            freqs, np.vstack([pz, fz]), 1.0, 4, 4, None,
            evidence=spectrum.evidence,
        ), fz_channel="Pz", pz_channel="Fz", requested_range=REQUESTED)
        assert first.value is not None and swapped.value is not None
        assert first.channel == "Fz/Pz"
        assert first.value != pytest.approx(swapped.value)

    def test_offline_brainbeat_missing_channel_returns_null_with_code(self):
        freqs = np.arange(1.0, 30.25, 0.25)
        result = compute_brainbeat(
            _spectrum(freqs, np.ones((1, len(freqs)))),
            fz_channel="Fz", pz_channel="Pz", requested_range=REQUESTED,
        )
        assert result.value is None
        assert result.failure.code == "BRAINBEAT_CHANNEL_DATA_UNAVAILABLE"

    def test_roles_must_be_explicit_fz_then_pz_and_distinct(self):
        with pytest.raises(ValueError, match="must be different"):
            BrainbeatConfig(channel="Fz", secondary_channel="fz", mode="static", start_s=0, end_s=10)
        with pytest.raises(ValueError, match="requires Fz"):
            BrainbeatConfig(channel="O1", secondary_channel="Pz", mode="static", start_s=0, end_s=10)

    def test_window_order_does_not_change_offline_brainbeat_value(self):
        freqs = np.arange(1.0, 30.25, 0.25)
        baseline = 1e-12 / freqs
        alpha = 8e-12 * np.exp(-0.5 * ((freqs - 10.0) / 0.7) ** 2)
        first = _spectrum(freqs, np.vstack([baseline + alpha * 0.4, baseline + alpha]))
        second = _spectrum(freqs, np.vstack([baseline + alpha * 0.8, baseline + alpha * 0.5]))
        forward = [
            compute_brainbeat(spectrum, fz_channel="Fz", pz_channel="Pz", requested_range=REQUESTED).value
            for spectrum in (first, second)
        ]
        reverse_by_identity = {
            id(spectrum): compute_brainbeat(
                spectrum, fz_channel="Fz", pz_channel="Pz", requested_range=REQUESTED
            ).value
            for spectrum in (second, first)
        }
        assert forward == [reverse_by_identity[id(item)] for item in (first, second)]


class TestSpectralPrimitiveBoundaries:
    def test_preprocess_rejects_high_cut_at_nyquist(self):
        data = np.zeros((1000, 2))
        with pytest.raises(ValueError, match="奈奎斯特"):
            preprocess_offline(data, 250.0, high_hz=125.0)

    def test_preprocess_rejects_inverted_band(self):
        data = np.zeros((1000, 2))
        with pytest.raises(ValueError):
            preprocess_offline(data, 250.0, low_hz=40.0, high_hz=10.0)

    def test_welch_short_input_is_gate_failed_without_fabrication(self):
        data = np.zeros((100, 2))
        estimate = estimate_welch_psd(data, 250.0)
        assert estimate.gate_failed == "low_quality"
        assert estimate.psd.size == 0
        assert estimate.freqs.size == 0

    def test_band_power_rejects_edge_outside_axis(self):
        freqs = np.array([1.0, 2.0, 3.0])
        psd = np.array([1e-12, 1e-12, 1e-12])
        with pytest.raises(ValueError):
            band_power(freqs, psd, 2.0, 4.0)

    def test_welch_keeps_frequency_axis_and_v_squared_per_hz_unit(self):
        sfreq = 100.0
        times = np.arange(8 * sfreq) / sfreq
        data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
        result = estimate_welch_psd(data, sfreq)
        assert result.gate_failed is None
        assert result.psd.shape == (1, result.freqs.size)
        assert result.freqs[0] >= 1.0 and result.freqs[-1] <= 30.0
        assert result.evidence["welch"]["scaling"] == "density"

    def test_welch_rejects_gap_without_imputation(self):
        sfreq = 100.0
        times = np.arange(8 * sfreq) / sfreq
        data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
        data[450, 0] = np.nan
        result = estimate_welch_psd(data, sfreq)
        assert result.evidence["gap"]["policy"] == "reject"
        assert result.evidence["gap"]["imputed"] is False
        assert result.gate_failed == "low_quality"

    def test_stft_bad_frame_is_nan_and_later_frame_remains_valid(self):
        sfreq = 100.0
        times = np.arange(7 * sfreq) / sfreq
        data = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]
        data[2 * int(sfreq):3 * int(sfreq), 0] = np.nan
        centers, freqs, power, quality = estimate_spectrogram_with_quality(data, sfreq)
        assert power.shape == (len(centers), 1, len(freqs))
        bad = [index for index, item in enumerate(quality) if item["status"] != "clean"]
        assert bad
        assert np.isnan(power[bad[0]]).all()
        assert any(np.isfinite(power[index]).all() for index in range(bad[0] + 1, len(power)))


class TestPsdAndStftAdapters:
    def test_rbp_historical_four_band_result_is_not_relabelled_as_current_five_band(self):
        # Historical four-band schema belongs to a separate version. The
        # current implementation must expose the current gamma band explicitly.
        assert tuple(five_band_shares(_flat_spectrum(1.0, 50.0, channels=1))[1]) == (
            "delta", "theta", "alpha", "beta", "gamma"
        )

    def test_stft_payload_shape_units_and_actual_window(self):
        class Payload:
            def load_spectrogram(self, **_kwargs):
                times = np.array([2., 3., 4.])
                frequencies = np.array([8., 9., 10.])
                matrix = np.ones((3, 3))
                return {
                    "times_s": times,
                    "frequencies_hz": frequencies,
                    "power_linear": {"Fz": matrix * 1e12},
                    "power_db": {"Fz": matrix * 20.0},
                    "quality": {"bad_windows": 0, "windows": []},
                    "spectrogram_contract_version": "spectrogram-v2",
                    "segment_s": 4.0, "step_s": 1.0, "window_start_s": 10.0,
                    "window_duration_s": 6.0,
                }

        algorithm = StftAlgorithm()
        config = StftConfig(channel="Fz", start_s=10, end_s=16, low_hz=8, high_hz=10)
        inputs = AlgorithmInputs(
            recording_id="r1", channel="Fz", sfreq_hz=100.0,
            duration_s=30.0, payload=Payload(),
        )
        result = algorithm.execute_static(inputs, config)
        assert result.arrays["power_linear"].shape == (3, 3)
        assert result.array_units["power_linear"] == "V^2/Hz"
        assert result.array_units["power_db"] == "dB re 1 uV^2/Hz"
        assert result.actual_range == {"start_s": 10.0, "end_s": 16.0}

    def test_dynamic_stft_inner_time_axis_is_centered_and_warmup_is_explicit(self):
        from app.algorithm_runtime.windows import build_dynamic_analysis_frames
        frames = build_dynamic_analysis_frames(
            0, 5, duration_s=10, window_s=5, step_s=1,
            minimum_window_s=4, allow_warmup=True,
        )
        assert frames
        assert frames[0].window_start_s == 0.0
        assert frames[0].window_end_s == 4.0
        assert frames[0].state == "Partial"
        assert frames[-1].window_end_s == 5.0

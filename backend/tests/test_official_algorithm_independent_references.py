"""Independent numerical references for migrated official algorithms.

These helpers intentionally do not import production spectral math. They use
only NumPy operations and the declared scientific formulas.
"""

from __future__ import annotations

import numpy as np

from app.algorithms.faa.official import compute_faa
from app.algorithms.iapf.official import estimate_iapf
from app.algorithms.theta_beta.official import metric_values
from app.scientific.primitives.spectral import SpectralEstimate
from app.scientific.primitives.spectral import estimate_spectrogram


def _reference_band_power(frequencies: np.ndarray, values: np.ndarray, low: float, high: float) -> np.ndarray:
    interior = (frequencies > low) & (frequencies < high)
    axis = np.concatenate(([low], frequencies[interior], [high]))
    rows = np.asarray(values).reshape((-1, len(frequencies)))
    interpolated = np.vstack([np.interp(axis, frequencies, row) for row in rows])
    result = np.trapezoid(interpolated, axis, axis=-1)
    return result.reshape(np.asarray(values).shape[:-1]) if np.asarray(values).ndim > 1 else result


def _reference_iapf(frequencies: np.ndarray, psd: np.ndarray) -> float:
    mean_psd = np.mean(psd, axis=0)
    fit = (frequencies >= 3.0) & (frequencies <= 30.0) & ~((frequencies >= 7.0) & (frequencies <= 13.0))
    slope, intercept = np.polyfit(np.log10(frequencies[fit]), np.log10(mean_psd[fit]), 1)
    alpha = (frequencies >= 7.0) & (frequencies <= 13.0)
    alpha_frequencies = frequencies[alpha]
    residual = np.clip(mean_psd[alpha] - 10.0 ** (intercept + slope * np.log10(alpha_frequencies)), 0.0, None)
    index = int(np.argmax(residual))
    return float(alpha_frequencies[index])


def _reference_faa(left: np.ndarray, right: np.ndarray, sfreq: float) -> float:
    epoch_samples = int(round(2.0 * sfreq))
    step = int(round(epoch_samples * 0.5))
    epochs_left, epochs_right = [], []
    for start in range(0, min(len(left), len(right)) - epoch_samples + 1, step):
        left_epoch = left[start:start + epoch_samples] - np.mean(left[start:start + epoch_samples])
        right_epoch = right[start:start + epoch_samples] - np.mean(right[start:start + epoch_samples])
        if np.isfinite(left_epoch).all() and np.isfinite(right_epoch).all():
            epochs_left.append(left_epoch)
            epochs_right.append(right_epoch)
    sample_index = np.arange(epoch_samples, dtype=float)
    window = 0.5 - 0.5 * np.cos(2.0 * np.pi * sample_index / epoch_samples)
    frequencies = np.fft.rfftfreq(epoch_samples, 1.0 / sfreq)

    def density(epochs: list[np.ndarray]) -> np.ndarray:
        transformed = np.fft.rfft(np.vstack(epochs) * window, axis=-1)
        values = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        values[:, 1:-1] *= 2.0
        return values.mean(axis=0)

    left_power = float(_reference_band_power(frequencies, density(epochs_left), 8.0, 13.0)[0])
    right_power = float(_reference_band_power(frequencies, density(epochs_right), 8.0, 13.0)[0])
    return float(np.log(right_power) - np.log(left_power))


def test_iapf_matches_independent_log_spectral_reference():
    frequencies = np.arange(1.0, 30.25, 0.25)
    baseline = 1e-12 / frequencies
    peak = baseline + 8e-12 * np.exp(-0.5 * ((frequencies - 10.0) / 0.5) ** 2)
    spectrum = SpectralEstimate(frequencies, np.vstack((peak, peak)), 1.0, 14, 14, None)
    result = estimate_iapf(spectrum)
    assert result.value == _reference_iapf(frequencies, spectrum.psd)
    assert result.source == "peak"


def test_rbp_and_theta_beta_match_independent_band_integration_reference():
    frequencies = np.arange(1.0, 30.25, 0.25)
    psd = np.ones((3, len(frequencies))) * 1e-12
    psd[0, (frequencies >= 4.0) & (frequencies <= 8.0)] *= 3.0
    psd[0, (frequencies >= 8.0) & (frequencies <= 12.0)] *= 2.0
    psd[1, (frequencies >= 8.0) & (frequencies <= 12.0)] *= 4.0
    psd[2, (frequencies >= 8.0) & (frequencies <= 12.0)] *= 4.0
    spectrum = SpectralEstimate(frequencies, psd, 1.0, 14, 14, None)

    totals = _reference_band_power(frequencies, psd, 1.0, 30.0)
    alpha = _reference_band_power(frequencies, psd, 8.0, 12.0)
    theta = _reference_band_power(frequencies, psd, 4.0, 8.0)
    beta = _reference_band_power(frequencies, psd, 13.0, 30.0)
    expected_brainbeat = (theta[0] / totals[0]) / (alpha[1] / totals[1])
    expected_fatigue = theta[0] / _reference_band_power(frequencies, psd, 12.0, 30.0)[0]
    result = metric_values(spectrum, 10.0)

    np.testing.assert_allclose(result["brainbeat"], expected_brainbeat, rtol=1e-12, atol=1e-24)
    np.testing.assert_allclose(result["fatigue"]["Fz"], expected_fatigue, rtol=1e-12, atol=1e-24)


def test_faa_matches_independent_paired_epoch_fft_reference():
    times = np.arange(30 * 100, dtype=float) / 100.0
    left = 10e-6 * np.sin(2.0 * np.pi * 10.0 * times)
    right = 20e-6 * np.sin(2.0 * np.pi * 10.0 * times)
    result = compute_faa(left, right, 100.0)
    expected = _reference_faa(left, right, 100.0)
    np.testing.assert_allclose(result["faa"], expected, rtol=1e-12, atol=1e-12)


def test_independent_reference_preserves_unavailable_short_faa_semantics():
    result = compute_faa(np.zeros(100), np.zeros(100), 100.0)
    assert result["faa"] is None
    assert result["reason"] == "too_short"


def test_stft_matches_independent_hann_fft_reference_with_exact_axes():
    sfreq = 100.0
    times = np.arange(6 * int(sfreq), dtype=float) / sfreq
    source = np.column_stack((
        10e-6 * np.sin(2.0 * np.pi * 10.0 * times),
        8e-6 * np.sin(2.0 * np.pi * 6.0 * times),
    ))
    centers, frequencies, actual = estimate_spectrogram(source, sfreq)

    segment_samples = 4 * int(sfreq)
    step_samples = int(sfreq)
    window = 0.5 - 0.5 * np.cos(2.0 * np.pi * np.arange(segment_samples) / segment_samples)
    full_frequencies = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    mask = (full_frequencies >= 1.0) & (full_frequencies <= 30.0)
    expected_frames = []
    for start in range(0, len(source) - segment_samples + 1, step_samples):
        frame = source[start:start + segment_samples]
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        expected_frames.append(density.T[:, mask])

    expected_centers = (np.arange(len(expected_frames)) * step_samples + segment_samples / 2.0) / sfreq
    np.testing.assert_array_equal(centers, expected_centers)
    np.testing.assert_array_equal(frequencies, full_frequencies[mask])
    np.testing.assert_allclose(actual, np.stack(expected_frames), rtol=0.0, atol=1e-24)

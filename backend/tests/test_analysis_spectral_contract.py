import numpy as np
from scipy import signal

from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT
from app.eeg_core.faa import compute_faa
from app.eeg_core.offline_metrics import metric_values
from app.eeg_core.spectral import (
    SpectralEstimate,
    band_power,
    estimate_spectrogram_with_quality,
    estimate_welch_psd,
    preprocess_offline,
)


def test_offline_preprocessing_matches_independent_scipy_reference():
    sfreq = 200.0
    times = np.arange(20 * int(sfreq)) / sfreq
    source = np.column_stack([
        200e-6 + 15e-6 * np.sin(2 * np.pi * 10 * times),
        -100e-6 + 8e-6 * np.sin(2 * np.pi * 6 * times),
    ])
    sos = signal.butter(4, [1.0, 30.0], btype="bandpass", fs=sfreq, output="sos")
    expected = signal.sosfiltfilt(sos, source, axis=0)

    np.testing.assert_allclose(preprocess_offline(source, sfreq), expected, rtol=1e-12, atol=1e-15)
    assert ANALYSIS_CONTRACT["algorithm_version"] == "offline-spectral-v3"


def test_welch_rejects_paired_artifact_epochs_without_joining_samples():
    sfreq = 100.0
    times = np.arange(8 * int(sfreq)) / sfreq
    values = np.column_stack([np.sin(2 * np.pi * 10 * times)] * 3) * 20e-6
    values[400, 1] = 300e-6

    spectrum = estimate_welch_psd(values, sfreq)

    assert spectrum.total_epochs == 3
    assert spectrum.clean_epochs == 1
    assert spectrum.signal_quality == 1 / 3
    assert spectrum.gate_failed == "low_quality"
    assert spectrum.psd.shape == (3, 0)


def test_welch_segments_overlap_inside_long_analysis_window():
    sfreq = 100.0
    times = np.arange(30 * int(sfreq)) / sfreq
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]

    spectrum = estimate_welch_psd(values, sfreq)

    assert spectrum.total_epochs == 14
    assert spectrum.clean_epochs == 14
    assert spectrum.gate_failed is None


def test_known_amplitude_sine_integrates_to_mean_square_power():
    sfreq = 200.0
    amplitude = 20e-6
    times = np.arange(30 * int(sfreq)) / sfreq
    values = (amplitude * np.sin(2 * np.pi * 10 * times))[:, None]

    spectrum = estimate_welch_psd(values, sfreq)
    power = float(np.trapezoid(spectrum.psd[0], spectrum.freqs))

    assert np.isclose(power, amplitude ** 2 / 2.0, rtol=0.02)


def test_four_second_spectrogram_row_matches_single_welch_psd():
    sfreq = 500.0
    times = np.arange(4 * int(sfreq)) / sfreq
    values = np.column_stack([
        20e-6 * np.sin(2 * np.pi * 10 * times) + 3e-6,
        8e-6 * np.sin(2 * np.pi * 6 * times),
    ])

    centers, frequencies, matrix, quality = estimate_spectrogram_with_quality(values, sfreq)
    static = estimate_welch_psd(values, sfreq)

    assert centers.tolist() == [2.0]
    assert quality[0]["status"] == "clean"
    np.testing.assert_allclose(matrix[0], static.psd, rtol=1e-12, atol=1e-18)


def test_band_integration_interpolates_boundaries_without_losing_area():
    freqs = np.arange(1.0, 31.0)
    psd = np.ones_like(freqs)

    delta = band_power(freqs, psd, 1.0, 4.0)
    theta = band_power(freqs, psd, 4.0, 8.0)

    assert delta == 3.0
    assert theta == 4.0
    assert delta + theta == band_power(freqs, psd, 1.0, 8.0)


def test_metric_formulas_use_fz_theta_over_pz_alpha_and_frontal_posterior_alpha():
    freqs = np.arange(1.0, 31.0)
    psd = np.ones((3, len(freqs)))
    psd[0, (freqs >= 4) & (freqs <= 8)] = 3.0
    psd[0, (freqs >= 8) & (freqs <= 12)] = 2.0
    psd[1, (freqs >= 8) & (freqs <= 12)] = 4.0
    psd[2, (freqs >= 8) & (freqs <= 12)] = 4.0
    spectrum = SpectralEstimate(freqs, psd, 1.0, 1, 1, None)

    result = metric_values(spectrum, 10.0)

    total = np.trapezoid(psd, freqs, axis=1)
    theta_fz = np.trapezoid(psd[0, (freqs >= 4) & (freqs <= 8)], freqs[(freqs >= 4) & (freqs <= 8)]) / total[0]
    alpha_pz = np.trapezoid(psd[1, (freqs >= 8) & (freqs <= 12)], freqs[(freqs >= 8) & (freqs <= 12)]) / total[1]
    assert np.isclose(result["brainbeat"], theta_fz / alpha_pz)
    assert result["spatial_distribution"] is not None


def test_shared_faa_uses_log_power_difference():
    sfreq = 100.0
    times = np.arange(12 * int(sfreq)) / sfreq
    f3 = 10e-6 * np.sin(2 * np.pi * 10 * times)
    f4 = 20e-6 * np.sin(2 * np.pi * 10 * times)
    report = compute_faa(f3, f4, sfreq)
    assert report["reason"] == ""
    assert np.isclose(report["faa"], np.log(4.0), atol=1e-12)

import numpy as np
from scipy import signal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT, LIVE_ANALYSIS_CONTRACT
from app.algorithms.faa.official import compute_faa
from app.algorithms.theta_beta.official import metric_values
from app.scientific.primitives.spectral import (
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
    assert "iapf_window_s" not in ANALYSIS_CONTRACT
    assert "iapf_step_s" not in ANALYSIS_CONTRACT
    assert ANALYSIS_CONTRACT["faa_static_scope"] == "exact_requested_absolute_range"
    assert LIVE_ANALYSIS_CONTRACT["iapf_lock_window_s"] == 30.0
    assert LIVE_ANALYSIS_CONTRACT["iapf_lock_attempt_step_s"] == 5.0
    assert LIVE_ANALYSIS_CONTRACT["faa_initial_discard_s"] == 12.0


def test_psd_frequency_range_changes_the_declared_filter_and_welch_axis():
    sfreq = 200.0
    times = np.arange(20 * int(sfreq)) / sfreq
    source = (20e-6 * np.sin(2 * np.pi * 40 * times) + 10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]

    filtered = preprocess_offline(source, sfreq, low_hz=0.5, high_hz=50.0)
    estimate = estimate_welch_psd(filtered, sfreq, low_hz=0.5, high_hz=50.0)

    assert estimate.gate_failed is None
    assert estimate.freqs[0] == 0.5
    assert estimate.freqs[-1] == 50.0
    assert estimate.psd.shape == (1, len(estimate.freqs))


def test_optional_powerline_notch_is_zero_phase_and_preserves_other_frequency():
    sfreq = 1000.0
    times = np.arange(20 * int(sfreq)) / sfreq
    source = np.column_stack([
        20e-6 * np.sin(2 * np.pi * 50 * times) + 10e-6 * np.sin(2 * np.pi * 10 * times),
    ])

    filtered = preprocess_offline(source, sfreq, low_hz=0.5, high_hz=50.0, notch_hz=50.0, notch_q=30.0)
    spectrum = np.abs(np.fft.rfft(filtered[:, 0]))
    fifty_power = spectrum[int(50.0 * len(filtered) / sfreq)]
    ten_power = spectrum[int(10.0 * len(filtered) / sfreq)]

    assert fifty_power < 0.1 * ten_power


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


def test_welch_records_input_gap_without_claiming_transform_padding():
    sfreq = 100.0
    values = np.ones((8 * int(sfreq), 1), dtype=float) * 10e-6
    values[200:220, 0] = np.nan

    spectrum = estimate_welch_psd(values, sfreq)

    assert spectrum.evidence["schema_version"] == "spectral-window-evidence-v1"
    assert spectrum.evidence["gap"]["detected"] is True
    assert spectrum.evidence["gap"]["non_finite_samples"] == 20
    assert spectrum.evidence["gap"]["imputed"] is False
    assert spectrum.evidence["transform_padding"] == {"used": False, "kind": "none", "samples": 0}


def test_welch_segments_overlap_inside_long_analysis_window():
    sfreq = 100.0
    times = np.arange(30 * int(sfreq)) / sfreq
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]

    spectrum = estimate_welch_psd(values, sfreq)

    assert spectrum.total_epochs == 14
    assert spectrum.clean_epochs == 14
    assert spectrum.gate_failed is None
    assert spectrum.evidence["welch"] == {
        "segment_s": 4.0,
        "segment_samples": 400,
        "overlap_fraction": 0.5,
        "overlap_samples": 200,
        "step_s": 2.0,
        "window": "hann",
        "scaling": "density",
    }


def test_welch_uses_declared_internal_overlap_for_each_clean_epoch():
    sfreq = 100.0
    times = np.arange(8 * int(sfreq)) / sfreq
    values = (10e-6 * np.sin(2 * np.pi * 10 * times))[:, None]

    spectrum = estimate_welch_psd(values, sfreq)
    expected_rows = []
    for start in (0, 200, 400):
        frequencies, row = signal.welch(
            values[start:start + 400, 0],
            fs=sfreq,
            window="hann",
            nperseg=400,
            noverlap=200,
            detrend="constant",
            scaling="density",
        )
        expected_rows.append(row)
    mask = (frequencies >= 1.0) & (frequencies <= 30.0)

    np.testing.assert_allclose(spectrum.freqs, frequencies[mask])
    expected = np.maximum(np.mean(expected_rows, axis=0), 1e-20)[mask]
    np.testing.assert_allclose(spectrum.psd[0], expected)


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


def test_spectrogram_bad_window_preserves_gap_evidence_and_no_zero_imputation():
    sfreq = 100.0
    values = np.ones((8 * int(sfreq), 1), dtype=float) * 10e-6
    values[400:420, 0] = np.nan

    centers, _frequencies, power, quality = estimate_spectrogram_with_quality(values, sfreq)

    assert centers.tolist() == [2.0, 3.0, 4.0, 5.0, 6.0]
    assert quality[1]["gap"]["detected"] is True
    assert quality[1]["gap"]["non_finite_samples"] == 20
    assert quality[1]["transform_padding"] == {"used": False, "kind": "none", "samples": 0}
    assert np.isnan(power[1]).all()


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

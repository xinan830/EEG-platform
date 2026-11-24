import numpy as np
from scipy import signal

from app.services.waveform_filter import DisplaySignalFilter


def test_filter_contract_matches_independent_golden_values():
    """独立复现显示契约；这里不导入生产滤波器，避免测试自我通过。"""
    sfreq = 500.0
    samples = np.arange(120, dtype=float)
    source = 0.0002 + 0.00001 * np.sin(2 * np.pi * 10 * samples / sfreq)
    source += 0.000004 * np.sin(2 * np.pi * 50 * samples / sfreq)

    notch_b, notch_a = signal.iirnotch(50.0, 30.0, sfreq)
    notch_sos = signal.tf2sos(notch_b, notch_a)
    band_sos = signal.butter(4, [0.5, 70.0], btype="bandpass", fs=sfreq, output="sos")
    notch_filtered, _ = signal.sosfilt(notch_sos, source, zi=signal.sosfilt_zi(notch_sos) * source[0])
    filtered, _ = signal.sosfilt(band_sos, notch_filtered, zi=signal.sosfilt_zi(band_sos) * notch_filtered[0])
    expected = np.array([2.71050543e-20, 5.16627528e-08, 3.85239033e-07, 4.01558732e-06,
                         4.02559182e-06, -5.43107010e-06, -4.11378430e-06, 7.28400034e-06])
    indices = [0, 1, 2, 10, 25, 50, 100, 119]
    np.testing.assert_allclose(filtered[indices], expected, rtol=1e-8, atol=1e-15)
    production = DisplaySignalFilter(sfreq, 1, notch_freq=50.0).process(source[:, None])[:, 0]
    np.testing.assert_allclose(production, filtered, rtol=1e-12, atol=1e-15)

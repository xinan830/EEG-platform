from types import SimpleNamespace

import numpy as np

from app.eeg_core.iapf_diagnostics import (
    iapf_attempt_log_line,
    iapf_waiting_log_line,
)
from app.eeg_core.realtime_spectral import (
    band_power,
    band_relative_power,
    clip_map,
    compute_hai,
    logistic_map,
    residual_spectrum,
    segment_brainbeat,
)


def test_processor_module_keeps_legacy_helper_exports():
    from app.eeg_core import processor

    assert processor.band_power is band_power
    assert processor.compute_hai is compute_hai
    assert processor.iapf_attempt_log_line is iapf_attempt_log_line
    assert processor.iapf_waiting_log_line is iapf_waiting_log_line


def test_realtime_spectral_helpers_keep_characterized_values():
    freqs = np.arange(0.0, 31.0)
    psd = freqs + 1.0

    assert band_power(freqs, psd, 1.0, 4.0) == 10.5
    assert band_power(freqs, psd, 4.1, 4.9) == 0.0
    relative = band_relative_power(freqs, psd)
    assert tuple(relative) == ("delta", "theta", "alpha", "beta")
    assert np.isclose(sum(relative.values()), 1.0)
    assert np.isclose(compute_hai(freqs, psd), np.log10(240.0 / 38.5))
    assert clip_map(0.5, 0.0, 1.0) == 50.0
    assert clip_map(np.nan, 0.0, 1.0) == 0
    assert logistic_map(0.0, 0.0, 1.0) == 50.0


def test_residual_and_segment_brainbeat_keep_characterized_values():
    freqs = np.arange(0.0, 31.0)
    psd = np.full(freqs.shape, 2.0)
    residual = residual_spectrum(freqs, psd, 0.0, 0.0)
    np.testing.assert_array_equal(residual, np.r_[2.0, np.ones(30)])
    assert residual_spectrum(freqs, psd, None, 1.0) is None

    fz_psd = np.ones_like(freqs)
    pz_psd = np.ones_like(freqs)
    assert np.isclose(segment_brainbeat(freqs, fz_psd, pz_psd, 10.0), 1.0)


def test_iapf_diagnostic_text_keeps_public_log_contract():
    result = SimpleNamespace(
        gate_failed=None,
        calibrated=True,
        iapf=10.25,
        clean_duration_s=25.0,
        total_duration_s=30.0,
        bad_duration_s=5.0,
        signal_quality=0.75,
        model_r2=0.9,
        model_error=0.1,
        cog=10.2,
        gaussian_cf=10.0,
        iapf_source="peak",
        peak_exists=True,
        peak_quality="good",
        peak_power=1.25,
        alpha_residual_ratio=0.5,
    )
    line = iapf_attempt_log_line(result, False, 2, 3, 10.1, 10.0, 15000, 500)
    assert line == (
        "[IAPF] locked=False calibrated=True gate=pass iapf=10.25Hz live=10.10Hz "
        "global=10.00Hz candidates=2/3 clean=25.00/30.00s bad=5.00s signal_quality=0.75 "
        "r2=0.900 mae=0.100 cog=10.20Hz peak=10.00Hz cog_minus_peak=0.20Hz "
        "source=peak peak_exists=True quality=good peak_power=1.250 alpha_ratio=0.500 buffer=30.00s"
    )
    assert iapf_waiting_log_line(250, 1000, 500) == (
        "[IAPF] waiting_buffer buffer=0.50/2.00s"
    )

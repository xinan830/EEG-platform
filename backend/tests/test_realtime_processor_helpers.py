import numpy as np

from app.eeg_core.realtime_spectral import (
    band_power,
    band_relative_power,
    clip_map,
    compute_hai,
    logistic_map,
    residual_spectrum,
    segment_brainbeat,
)


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

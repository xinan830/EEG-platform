"""Pure legacy spectral reference helpers retained for validation tests."""

import numpy as np

from app.eeg_core.official_algorithms.brainbeat import segment_brainbeat


RBP_BAND_EDGES = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0),
    "beta": (13.0, 30.0),
}

HAI_LOW_BAND = (1.0, 8.0)
HAI_BETA_BAND = (13.0, 25.0)


def band_power(freqs, pxx, f_low, f_high):
    """Integrate PSD over an inclusive frequency interval."""
    freqs = np.asarray(freqs, dtype=float)
    pxx = np.asarray(pxx, dtype=float)
    idx = np.logical_and(freqs >= f_low, freqs <= f_high)
    if np.sum(idx) < 2:
        return 0.0
    return float(np.trapezoid(pxx[idx], freqs[idx]))


def band_relative_power(freqs, pxx):
    """Return delta/theta/alpha/beta powers normalized to a sum of one."""
    powers = {
        name: band_power(freqs, pxx, low, high)
        for name, (low, high) in RBP_BAND_EDGES.items()
    }
    total = sum(powers.values())
    if not np.isfinite(total) or total <= 0:
        return {name: 0.0 for name in RBP_BAND_EDGES}
    return {name: powers[name] / total for name in RBP_BAND_EDGES}


def compute_hai(freqs, pxx):
    """Compute log10(beta[13,25] / low[1,8]); return None if undefined."""
    beta = band_power(freqs, pxx, *HAI_BETA_BAND)
    low = band_power(freqs, pxx, *HAI_LOW_BAND)
    if beta <= 0.0 or low <= 0.0:
        return None
    value = float(np.log10(beta / low))
    return value if np.isfinite(value) else None


def residual_spectrum(freqs, psd, offset, exponent):
    """Subtract a fitted 1/f component in linear power space and clip at zero."""
    if offset is None or exponent is None or not (
        np.isfinite(offset) and np.isfinite(exponent)
    ):
        return None
    freqs = np.asarray(freqs, dtype=float)
    psd = np.asarray(psd, dtype=float)
    valid = freqs > 0
    ap_lin = np.zeros_like(freqs)
    ap_lin[valid] = np.power(
        10.0,
        offset - exponent * np.log10(freqs[valid]),
    )
    return np.clip(psd - ap_lin, 0.0, None)


def clip_map(v, v_min, v_max):
    """Map a value into 0-100 using fixed empirical bounds."""
    if np.isnan(v) or np.isinf(v):
        return 0
    mapped = (v - v_min) / (v_max - v_min) * 100
    return max(0, min(100, mapped))


def logistic_map(v, center, slope):
    """Map a value into 0-100 through a logistic curve."""
    if np.isnan(v) or np.isinf(v):
        return 0
    return 100.0 / (1.0 + np.exp(-(v - center) / slope))

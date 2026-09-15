"""Frozen single-frame BrainBeat calculation for the realtime chain."""

from __future__ import annotations

import numpy as np


def _band_power(freqs, pxx, f_low, f_high) -> float:
    frequencies = np.asarray(freqs, dtype=float)
    power = np.asarray(pxx, dtype=float)
    mask = (frequencies >= f_low) & (frequencies <= f_high)
    if int(mask.sum()) < 2:
        return 0.0
    return float(np.trapezoid(power[mask], frequencies[mask]))


def segment_brainbeat(freqs, fz_psd, pz_psd, iapf, epsilon=1e-20):
    """Compute Fz relative theta divided by Pz relative alpha."""
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = _band_power(freqs, fz_psd, 1.0, 30.0) + epsilon
    total_pz = _band_power(freqs, pz_psd, 1.0, 30.0) + epsilon
    theta_fz = _band_power(freqs, fz_psd, theta_low, theta_high) / total_fz
    alpha_pz = _band_power(freqs, pz_psd, alpha_low, alpha_high) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))

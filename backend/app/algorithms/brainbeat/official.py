"""BrainBeat formulas over paired Fz/Pz PSDs in V^2/Hz."""

from __future__ import annotations

import numpy as np
from app.scientific.primitives.spectral import band_power


def classic_brainbeat(freqs, fz_psd, pz_psd):
    """Return absolute Fz 4-8 Hz power divided by absolute Pz 8-12 Hz power."""
    theta_fz = float(band_power(freqs, fz_psd, 4.0, 8.0))
    alpha_pz = float(band_power(freqs, pz_psd, 8.0, 12.0))
    if not np.isfinite(theta_fz) or not np.isfinite(alpha_pz) or alpha_pz <= 0.0:
        raise ValueError("classic BrainBeat bands must have finite power and a positive Pz alpha denominator")
    return float(theta_fz / alpha_pz)


def segment_brainbeat(freqs, fz_psd, pz_psd, iapf, epsilon=1e-20):
    """Return the individualized Fz relative theta / Pz relative alpha ratio."""
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = float(band_power(freqs, fz_psd, 1.0, 30.0)) + epsilon
    total_pz = float(band_power(freqs, pz_psd, 1.0, 30.0)) + epsilon
    theta_fz = float(band_power(freqs, fz_psd, theta_low, theta_high)) / total_fz
    alpha_pz = float(band_power(freqs, pz_psd, alpha_low, alpha_high)) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))

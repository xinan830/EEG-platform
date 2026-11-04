"""Single-window Brainbeat ratio, owned by the current offline algorithm.

Inputs are ordered frequency bins in Hz and Fz/Pz PSDs in V^2/Hz.
Band power uses the shared boundary-interpolated trapezoidal integral; this
calculation has no cross-window state or realtime EMA.
"""

from __future__ import annotations

import numpy as np
from app.scientific.primitives.spectral import band_power


def segment_brainbeat(freqs, fz_psd, pz_psd, iapf, epsilon=1e-20):
    """Return the dimensionless Fz relative theta / Pz relative alpha ratio."""
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = float(band_power(freqs, fz_psd, 1.0, 30.0)) + epsilon
    total_pz = float(band_power(freqs, pz_psd, 1.0, 30.0)) + epsilon
    theta_fz = float(band_power(freqs, fz_psd, theta_low, theta_high)) / total_fz
    alpha_pz = float(band_power(freqs, pz_psd, alpha_low, alpha_high)) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))

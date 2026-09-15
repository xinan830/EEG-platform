"""Frozen IAPF-relative Theta/Beta and related three-channel metric formulas."""

from __future__ import annotations

import numpy as np

from app.eeg_core.spectral import SpectralEstimate, band_power


def metric_values(spectrum: SpectralEstimate, iapf: float) -> dict[str, object]:
    """Ordered Fz/Pz/Oz output; source channel mapping is explicit upstream."""
    if spectrum.gate_failed or spectrum.psd.shape[0] < 3:
        return {"relaxation": None, "spatial_distribution": None, "brainbeat": None, "fatigue": {}, "narrow_alpha": None}
    freqs, psds = spectrum.freqs, spectrum.psd
    totals = np.asarray(band_power(freqs, psds, 1.0, 30.0))
    posterior_psd = np.mean(psds[1:3], axis=0)
    posterior_total = float(band_power(freqs, posterior_psd, 1.0, 30.0))
    narrow = float(band_power(freqs, posterior_psd, iapf - 1.0, iapf + 1.0))
    posterior_alpha = float(band_power(freqs, posterior_psd, iapf - 2.0, iapf + 2.0))
    frontal_alpha = float(band_power(freqs, psds[0], iapf - 2.0, iapf + 2.0))
    posterior_ratio = posterior_alpha / posterior_total if posterior_total > 0 else 0.0
    frontal_ratio = frontal_alpha / totals[0] if totals[0] > 0 else 0.0
    theta = np.asarray(band_power(freqs, psds, max(4.0, iapf - 6.0), iapf - 2.0))
    beta = np.asarray(band_power(freqs, psds, iapf + 2.0, 30.0))
    pz_alpha_ratio = float(band_power(freqs, psds[1], iapf - 2.0, iapf + 2.0)) / totals[1] if totals[1] > 0 else 0.0
    theta_fz_ratio = theta[0] / totals[0] if totals[0] > 0 else 0.0
    return {
        "relaxation": narrow / posterior_total if posterior_total > 0 else None,
        "spatial_distribution": float(frontal_ratio / posterior_ratio) if posterior_ratio >= 0.02 else None,
        "brainbeat": float(theta_fz_ratio / pz_alpha_ratio) if pz_alpha_ratio > 0 else None,
        "fatigue": {name: float(theta[index] / beta[index]) for index, name in enumerate(("Fz", "Pz", "Oz")) if beta[index] > 0},
        "narrow_alpha": [float(band_power(freqs, psds[index], iapf - 1.0, iapf + 1.0) / totals[index]) if totals[index] > 0 else 0.0 for index in range(3)],
    }

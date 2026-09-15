"""Frozen official IAPF estimate: aperiodic fit, Peak/COG and gate semantics."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.eeg_core.spectral import SpectralEstimate


@dataclass(frozen=True)
class IAPFEstimate:
    value: float | None
    source: str | None
    gate_failed: str | None
    model_r2: float | None
    model_error: float | None
    peak_hz: float | None
    cog: float | None


def estimate_iapf(spectrum: SpectralEstimate) -> IAPFEstimate:
    if spectrum.gate_failed or not len(spectrum.freqs):
        return IAPFEstimate(None, None, spectrum.gate_failed or "low_quality", None, None, None, None)
    psd = spectrum.psd.mean(axis=0)
    fit_mask = (spectrum.freqs >= 3.0) & (spectrum.freqs <= 30.0)
    fit_mask &= ~((spectrum.freqs >= 7.0) & (spectrum.freqs <= 13.0))
    log_f = np.log10(spectrum.freqs[fit_mask])
    log_p = np.log10(psd[fit_mask])
    slope, intercept = np.polyfit(log_f, log_p, 1)
    fitted = intercept + slope * log_f
    residual = log_p - fitted
    error = float(np.mean(np.abs(residual)))
    total = float(np.sum((log_p - log_p.mean()) ** 2))
    r2 = float(1.0 - np.sum(residual ** 2) / total) if total > 0 else None
    alpha_mask = (spectrum.freqs >= 7.0) & (spectrum.freqs <= 13.0)
    alpha_freqs, alpha_psd = spectrum.freqs[alpha_mask], psd[alpha_mask]
    if not len(alpha_freqs):
        return IAPFEstimate(None, None, "no_peak_no_cog", r2, error, None, None)
    baseline = np.power(10.0, intercept + slope * np.log10(alpha_freqs))
    residual_power = np.clip(alpha_psd - baseline, 0.0, None)
    cog = float(np.sum(alpha_freqs * residual_power) / residual_power.sum()) if residual_power.sum() > 0 else None
    peak_index = int(np.argmax(residual_power))
    peak = float(alpha_freqs[peak_index])
    prominence = float(residual_power[peak_index] / max(baseline[peak_index], 1e-20))
    if prominence >= 0.20:
        return IAPFEstimate(peak, "peak", None, r2, error, peak, cog)
    if cog is not None:
        return IAPFEstimate(cog, "cog", None, r2, error, peak, cog)
    return IAPFEstimate(None, None, "no_peak_no_cog", r2, error, peak, cog)

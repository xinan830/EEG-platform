"""基于统一 PSD 口径的 IAPF 与离线指标公式。"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.eeg_core.spectral import SpectralEstimate, band_power


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
    alpha_freqs = spectrum.freqs[alpha_mask]
    alpha_psd = psd[alpha_mask]
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


def metric_values(spectrum: SpectralEstimate, iapf: float) -> dict[str, object]:
    """通道顺序固定为 Fz/Pz/Oz；返回未经评分映射的可解释比值。"""
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

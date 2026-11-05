"""Official IAPF calculation for the Runtime-facing IAPF module."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from scipy.signal import find_peaks

from app.scientific.primitives.spectral import SpectralEstimate

PEAK_MINIMUM_PROMINENCE_RATIO = 0.20
PEAK_MINIMUM_WIDTH_BINS = 1.2
COG_MINIMUM_EXCESS_RATIO = 0.20
COG_MINIMUM_EFFECTIVE_BINS = 3.0


@dataclass(frozen=True)
class IAPFEstimate:
    value: float | None
    source: str | None
    gate_failed: str | None
    model_r2: float | None
    model_error: float | None
    peak_hz: float | None
    cog: float | None
    peak_prominence_ratio: float | None = None
    peak_width_bins: float | None = None
    cog_excess_ratio: float | None = None
    cog_effective_bins: float | None = None


def estimate_iapf(spectrum: SpectralEstimate) -> IAPFEstimate:
    if spectrum.gate_failed or not len(spectrum.freqs):
        return IAPFEstimate(None, None, spectrum.gate_failed or "low_quality", None, None, None, None)
    psd = spectrum.psd.mean(axis=0)
    fit_mask = (spectrum.freqs >= 3.0) & (spectrum.freqs <= 30.0)
    fit_mask &= ~((spectrum.freqs >= 7.0) & (spectrum.freqs <= 13.0))
    fit_mask &= np.isfinite(spectrum.freqs) & np.isfinite(psd) & (spectrum.freqs > 0.0) & (psd > 0.0)
    if np.count_nonzero(fit_mask) < 3:
        return IAPFEstimate(None, None, "invalid_aperiodic_fit", None, None, None, None)
    log_f = np.log10(spectrum.freqs[fit_mask])
    log_p = np.log10(psd[fit_mask])
    slope, intercept = np.polyfit(log_f, log_p, 1)
    fitted = intercept + slope * log_f
    residual = log_p - fitted
    error = float(np.mean(np.abs(residual)))
    total = float(np.sum((log_p - log_p.mean()) ** 2))
    r2 = float(1.0 - np.sum(residual ** 2) / total) if total > 0 else None
    if not np.isfinite(slope) or not np.isfinite(intercept) or not np.isfinite(error) or r2 is None or not np.isfinite(r2):
        return IAPFEstimate(None, None, "invalid_aperiodic_fit", r2, error, None, None)
    alpha_mask = (spectrum.freqs >= 7.0) & (spectrum.freqs <= 13.0)
    alpha_freqs, alpha_psd = spectrum.freqs[alpha_mask], psd[alpha_mask]
    if not len(alpha_freqs):
        return IAPFEstimate(None, None, "no_peak_no_cog", r2, error, None, None)
    baseline = np.power(10.0, intercept + slope * np.log10(alpha_freqs))
    residual_power = np.clip(alpha_psd - baseline, 0.0, None)
    residual_sum = float(residual_power.sum())
    cog = float(np.sum(alpha_freqs * residual_power) / residual_sum) if residual_sum > 0.0 else None
    cog_excess_ratio = residual_sum / max(float(baseline.sum()), 1e-20)
    cog_effective_bins = residual_sum ** 2 / float(np.sum(residual_power ** 2)) if residual_sum > 0.0 else 0.0
    peaks, properties = find_peaks(residual_power, prominence=0.0, width=PEAK_MINIMUM_WIDTH_BINS)
    peak_candidates = [
        (int(index), float(prominence / max(baseline[index], 1e-20)), float(width))
        for index, prominence, width in zip(
            peaks, properties["prominences"], properties["widths"], strict=True
        )
    ]
    accepted_peaks = [
        item for item in peak_candidates
        if item[1] >= PEAK_MINIMUM_PROMINENCE_RATIO
    ]
    peak_index = max(accepted_peaks, key=lambda item: residual_power[item[0]])[0] if accepted_peaks else None
    peak = float(alpha_freqs[peak_index]) if peak_index is not None else None
    prominence_ratio = next((item[1] for item in accepted_peaks if item[0] == peak_index), None)
    peak_width_bins = next((item[2] for item in accepted_peaks if item[0] == peak_index), None)
    if peak_index is not None:
        return IAPFEstimate(peak, "peak", None, r2, error, peak, cog,
                            prominence_ratio, peak_width_bins, cog_excess_ratio, cog_effective_bins)
    if (cog is not None and cog_excess_ratio >= COG_MINIMUM_EXCESS_RATIO
            and cog_effective_bins >= COG_MINIMUM_EFFECTIVE_BINS):
        return IAPFEstimate(cog, "cog", None, r2, error, peak, cog,
                            None, None, cog_excess_ratio, cog_effective_bins)
    reason = "no_supported_alpha_peak" if cog is not None else "no_peak_no_cog"
    return IAPFEstimate(None, None, reason, r2, error, peak, cog,
                        None, None, cog_excess_ratio, cog_effective_bins)

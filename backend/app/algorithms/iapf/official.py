"""Official IAPF calculation for the Runtime-facing IAPF module."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from scipy.signal import find_peaks, savgol_filter

from app.scientific.primitives.spectral import SpectralEstimate

SAVGOL_WINDOW_BINS = 5
SAVGOL_POLYORDER = 2
PEAK_MINIMUM_PROMINENCE_RATIO = 0.20
PEAK_MINIMUM_WIDTH_BINS = 1.2
PEAK_MINIMUM_RAW_SUPPORT_RATIO = 1.25
PEAK_MINIMUM_DOMINANCE_RATIO = 1.20
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
    split_peak: bool = False
    secondary_peak_hz: float | None = None
    peak_dominance_ratio: float | None = None


def _refine_peak_hz(freqs: np.ndarray, curve: np.ndarray, index: int) -> float:
    if index <= 0 or index >= len(freqs) - 1:
        return float(freqs[index])
    y_l, y_c, y_r = float(curve[index - 1]), float(curve[index]), float(curve[index + 1])
    denom = y_l - 2.0 * y_c + y_r
    delta = float(np.clip(0.5 * (y_l - y_r) / denom, -0.5, 0.5)) if denom < -1e-30 else 0.0
    step_hz = 0.5 * float(freqs[index + 1] - freqs[index - 1])
    return float(np.clip(float(freqs[index]) + delta * step_hz, float(freqs[0]), float(freqs[-1])))


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

    smoothed_residual = (
        np.clip(savgol_filter(residual_power, window_length=SAVGOL_WINDOW_BINS, polyorder=SAVGOL_POLYORDER), 0.0, None)
        if len(alpha_freqs) >= SAVGOL_WINDOW_BINS
        else residual_power
    )
    peaks, properties = find_peaks(smoothed_residual, prominence=0.0, width=PEAK_MINIMUM_WIDTH_BINS)
    accepted_peaks: list[tuple[int, float, float]] = []
    for index, prominence, width in zip(peaks, properties["prominences"], properties["widths"], strict=True):
        idx = int(index)
        prom_ratio = float(prominence / max(baseline[idx], 1e-20))
        if prom_ratio < PEAK_MINIMUM_PROMINENCE_RATIO:
            continue
        raw_window = residual_power[max(0, idx - 1):min(len(residual_power), idx + 2)]
        raw_max = float(raw_window.max()) if len(raw_window) else 0.0
        if raw_max <= 0.0 or float(raw_window.sum()) / raw_max < PEAK_MINIMUM_RAW_SUPPORT_RATIO:
            continue
        accepted_peaks.append((idx, prom_ratio, float(width)))

    accepted_peaks.sort(key=lambda item: (float(smoothed_residual[item[0]]), item[1]), reverse=True)
    peak: float | None = None
    prominence_ratio: float | None = None
    peak_width_bins: float | None = None
    secondary_peak: float | None = None
    dominance_ratio: float | None = None
    split_peak = False
    if accepted_peaks:
        top_index, prominence_ratio, peak_width_bins = accepted_peaks[0]
        peak = _refine_peak_hz(alpha_freqs, smoothed_residual, top_index)
        if len(accepted_peaks) >= 2:
            second_index = accepted_peaks[1][0]
            secondary_peak = _refine_peak_hz(alpha_freqs, smoothed_residual, second_index)
            dominance_ratio = float(smoothed_residual[top_index]) / max(float(smoothed_residual[second_index]), 1e-20)
            split_peak = dominance_ratio < PEAK_MINIMUM_DOMINANCE_RATIO

    if accepted_peaks and not split_peak:
        return IAPFEstimate(
            peak, "peak", None, r2, error, peak, cog,
            prominence_ratio, peak_width_bins, cog_excess_ratio, cog_effective_bins,
            split_peak=False, secondary_peak_hz=secondary_peak, peak_dominance_ratio=dominance_ratio,
        )
    if (cog is not None and cog_excess_ratio >= COG_MINIMUM_EXCESS_RATIO
            and cog_effective_bins >= COG_MINIMUM_EFFECTIVE_BINS):
        return IAPFEstimate(
            cog, "cog", None, r2, error, peak, cog,
            prominence_ratio if split_peak else None,
            peak_width_bins if split_peak else None,
            cog_excess_ratio, cog_effective_bins,
            split_peak=split_peak, secondary_peak_hz=secondary_peak, peak_dominance_ratio=dominance_ratio,
        )
    reason = "ambiguous_split_alpha_peak" if split_peak else ("no_supported_alpha_peak" if cog is not None else "no_peak_no_cog")
    return IAPFEstimate(
        None, None, reason, r2, error, peak, cog,
        None, None, cog_excess_ratio, cog_effective_bins,
        split_peak=split_peak, secondary_peak_hz=secondary_peak, peak_dominance_ratio=dominance_ratio,
    )

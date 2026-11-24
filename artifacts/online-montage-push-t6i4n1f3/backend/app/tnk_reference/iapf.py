"""Independent specparam reference for the ANT IAPF fit and peak selection."""

from __future__ import annotations

import numpy as np


def fit_fixed_spectrum(freqs_hz: np.ndarray, psd_v2_per_hz: np.ndarray) -> dict[str, object]:
    """Fit ANT's fixed aperiodic/Gaussian peak model over 3-30 Hz.

    This uses the scientific dependency and refuses missing specparam rather
    than switching to FOOOF, since that would change the reference identity.
    """
    from specparam import SpectralModel

    freqs = np.asarray(freqs_hz, dtype=np.float64)
    psd = np.asarray(psd_v2_per_hz, dtype=np.float64)
    if freqs.ndim != 1 or psd.ndim != 1 or freqs.shape != psd.shape:
        raise ValueError("IAPF fit requires equal-length frequency and PSD vectors")
    if not np.isfinite(freqs).all() or not np.isfinite(psd).all() or np.any(psd <= 0):
        raise ValueError("IAPF fit requires finite positive PSD values")
    selected = (freqs >= 3.0) & (freqs <= 30.0)
    fit_freqs, fit_psd = freqs[selected], psd[selected]
    if len(fit_freqs) < 3:
        raise ValueError("IAPF fit range has insufficient frequency bins")

    model = SpectralModel(
        peak_width_limits=[1.0, 8.0], max_n_peaks=4,
        peak_threshold=1.5, verbose=False,
    )
    model.fit(fit_freqs, fit_psd, [3.0, 30.0])
    params = model.results.params
    metrics = {metric.measure: metric.result for metric in model.results.metrics.metrics}
    peaks = np.asarray(params.periodic._fit, dtype=np.float64)
    if peaks.size == 0:
        peaks = np.empty((0, 3), dtype=np.float64)
    elif peaks.ndim == 1:
        peaks = peaks.reshape(1, -1)
    return {
        "fit_freqs_hz": fit_freqs,
        "aperiodic_fit_log10": np.asarray(model.results.model._ap_fit, dtype=np.float64),
        "model_fit_log10": np.asarray(model.results.model.modeled_spectrum, dtype=np.float64),
        "aperiodic_offset": float(params.aperiodic._fit[0]),
        "aperiodic_exponent": float(params.aperiodic._fit[1]),
        "r_squared": float(metrics["rsquared"]),
        "mae": float(metrics["mae"]),
        "peaks_cf_power_bw": peaks[:, :3],
    }


def select_iapf_peak(fit: dict[str, object], alpha_range_hz: tuple[float, float] = (7.0, 13.0)) -> dict[str, object]:
    """Select the highest-power modeled peak inside the alpha range."""
    low, high = alpha_range_hz
    peaks = np.asarray(fit["peaks_cf_power_bw"], dtype=np.float64)
    candidates = peaks[(peaks[:, 0] >= low) & (peaks[:, 0] <= high)] if peaks.size else peaks
    peak = candidates[np.argmax(candidates[:, 1])] if len(candidates) else None
    return {
        "gaussian_cf_hz": None if peak is None else float(peak[0]),
        "peak_power": 0.0 if peak is None else max(0.0, float(peak[1])),
        "peak_bandwidth_hz": None if peak is None else float(peak[2]),
        "peak_exists": peak is not None,
    }


def alpha_residual_cog(freqs_hz: np.ndarray, psd_v2_per_hz: np.ndarray,
                       fit: dict[str, object], alpha_range_hz: tuple[float, float] = (7.0, 13.0)) -> float | None:
    """Compute ANT's positive linear residual center of gravity in alpha."""
    freqs = np.asarray(freqs_hz, dtype=np.float64)
    psd = np.asarray(psd_v2_per_hz, dtype=np.float64)
    fit_freqs = np.asarray(fit["fit_freqs_hz"], dtype=np.float64)
    ap_fit = np.asarray(fit["aperiodic_fit_log10"], dtype=np.float64)
    if freqs.ndim != 1 or psd.ndim != 1 or freqs.shape != psd.shape:
        raise ValueError("IAPF CoG requires equal-length frequency and PSD vectors")
    if not np.isfinite(freqs).all() or not np.isfinite(psd).all() or np.any(psd < 0):
        raise ValueError("IAPF CoG requires finite non-negative PSD values")
    if fit_freqs.size == 0 or fit_freqs.shape != ap_fit.shape:
        return None
    band = (fit_freqs >= alpha_range_hz[0]) & (fit_freqs <= alpha_range_hz[1])
    residual = np.clip(np.interp(fit_freqs, freqs, psd) - np.power(10.0, ap_fit), 0.0, None)
    weights = residual[band]
    if not np.isfinite(weights).all() or weights.sum() <= 0:
        return None
    return float(np.sum(fit_freqs[band] * weights) / np.sum(weights))


def decide_iapf(fit: dict[str, object], cog_hz: float | None, *,
                minimum_r_squared: float = 0.80, maximum_mae: float = 0.20,
                minimum_peak_prominence: float = 0.20,
                fallback_hz: float = 10.0) -> dict[str, object]:
    """Apply ANT's fit gate, prominent-peak preference, and CoG fallback."""
    if fit["r_squared"] < minimum_r_squared:
        return {"value_hz": fallback_hz, "calibrated": False, "reason": "low_r2", "source": None}
    if fit["mae"] > maximum_mae:
        return {"value_hz": fallback_hz, "calibrated": False, "reason": "high_mae", "source": None}
    peak = select_iapf_peak(fit)
    if peak["peak_exists"] and peak["peak_power"] >= minimum_peak_prominence:
        return {"value_hz": peak["gaussian_cf_hz"], "calibrated": True, "reason": None, "source": "peak"}
    if cog_hz is not None and np.isfinite(cog_hz):
        return {"value_hz": float(cog_hz), "calibrated": True, "reason": None, "source": "cog"}
    return {"value_hz": fallback_hz, "calibrated": False, "reason": "no_peak_no_cog", "source": None}

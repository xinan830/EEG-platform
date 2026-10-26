"""Independent stateless TNK metric references (no production calculator imports)."""

from __future__ import annotations

import numpy as np

from .primitives import band_power


RBP_BANDS = {"delta": (1.0, 4.0), "theta": (4.0, 8.0),
             "alpha": (8.0, 13.0), "beta": (13.0, 30.0)}


def rbp(freqs: np.ndarray, psd: np.ndarray) -> dict[str, float] | None:
    powers = {name: band_power(freqs, psd, *bounds) for name, bounds in RBP_BANDS.items()}
    denominator = sum(powers.values())
    if not np.isfinite(denominator) or denominator <= 0:
        return None
    return {name: value / denominator for name, value in powers.items()}


def hai(freqs: np.ndarray, psd: np.ndarray) -> float | None:
    low = band_power(freqs, psd, 1.0, 8.0)
    beta = band_power(freqs, psd, 13.0, 25.0)
    if low <= 0 or beta <= 0:
        return None
    result = float(np.log10(beta / low))
    return result if np.isfinite(result) else None


def faa(psd: np.ndarray, freqs: np.ndarray, *, clean_epochs: int,
        min_clean_epochs: int = 10) -> dict[str, object]:
    """FAA from paired epoch PSD; consumes the independent output of faa_epoch_psd."""
    matrix = np.asarray(psd, dtype=np.float64)
    frequency_axis = np.asarray(freqs, dtype=np.float64)
    if not np.isfinite(frequency_axis).all() or frequency_axis.ndim != 1:
        raise ValueError("FAA frequency axis must be a finite vector")
    if matrix.ndim != 2 or matrix.shape[0] != 2 or matrix.shape[1] != len(freqs):
        raise ValueError("FAA reference expects a 2-channel epoch-averaged PSD")
    if not np.isfinite(matrix).all() or clean_epochs < 0 or min_clean_epochs < 1:
        raise ValueError("FAA PSD and clean-epoch counts are invalid")
    clean = int(clean_epochs)
    if clean < min_clean_epochs:
        return {"faa": None, "reason": "too_few_clean_epochs", "clean_epochs": clean}
    f3 = band_power(freqs, matrix[0], 8.0, 13.0)
    f4 = band_power(freqs, matrix[1], 8.0, 13.0)
    if f3 <= 0 or f4 <= 0:
        return {"faa": None, "reason": "band_truncated", "clean_epochs": clean}
    value = float(np.log(f4) - np.log(f3))
    return {"faa": value, "p_f3_v2": f3, "p_f4_v2": f4,
            "reason": None, "clean_epochs": clean}


def relaxation(alpha_power: float, total_power: float) -> float | None:
    if not np.isfinite(alpha_power) or not np.isfinite(total_power) or total_power <= 0:
        return None
    ratio = alpha_power / total_power
    return float(100.0 / (1.0 + np.exp(-(ratio - 0.15) / 0.055)))


def spatial_distribution(frontal_ratio: float, posterior_ratio: float) -> dict[str, object]:
    if not np.isfinite(frontal_ratio) or not np.isfinite(posterior_ratio) or posterior_ratio <= 0:
        return {"value": 0.0, "valid": False}
    ratio = frontal_ratio / posterior_ratio
    value = float(100.0 / (1.0 + np.exp(-(ratio - 0.45) / 0.15)))
    return {"value": value, "valid": posterior_ratio >= 0.02}


def composite_meditation(sd: float, stability: float, *, sd_valid: bool,
                         stability_ready: bool) -> dict[str, object]:
    if stability_ready and sd_valid:
        value, weights = 0.8 * stability + 0.2 * sd, {"stability": 0.8, "spatial_distribution": 0.2}
    elif stability_ready:
        value, weights = stability, {"stability": 1.0, "spatial_distribution": 0.0}
    else:
        value, weights = sd, {"stability": 0.0, "spatial_distribution": 1.0}
    return {"value": float(value), "weights": weights,
            "quality": "clean" if stability_ready and sd_valid else "degraded"}


def stability_score(channel_values: dict[str, float], *,
                    ready_channels: set[str],
                    weights: dict[str, float] | None = None) -> dict[str, object]:
    """ANT weighted channel stability mapped from [0.40, 0.90] to 0-100."""
    weight_by_channel = weights or {"Fz": 0.2, "Pz": 0.4, "Oz": 0.4}
    active = [name for name in weight_by_channel if name in channel_values]
    if not active:
        return {"value": 50.0, "ready": False, "channels": []}
    total_weight = sum(weight_by_channel[name] for name in active)
    normalized = sum(weight_by_channel[name] * channel_values[name] for name in active) / total_weight
    score = float(np.clip((normalized - 0.40) / (0.90 - 0.40) * 100.0, 0.0, 100.0))
    return {"value": score, "ready": all(name in ready_channels for name in active),
            "channels": active, "normalized_stability": float(normalized)}


def brainbeat_ratio(theta_fz: float, alpha_pz: float) -> float | None:
    if not np.isfinite(theta_fz) or not np.isfinite(alpha_pz) or theta_fz <= 0 or alpha_pz <= 0:
        return None
    return float(theta_fz / alpha_pz)


def dynamic_bands(iapf_hz: float) -> dict[str, tuple[float, float]]:
    if not np.isfinite(iapf_hz) or iapf_hz <= 0:
        raise ValueError("IAPF must be a positive finite frequency")
    return {
        "theta": (max(4.0, iapf_hz - 6.0), iapf_hz - 2.0),
        "alpha": (iapf_hz - 2.0, iapf_hz + 2.0),
        "beta": (iapf_hz + 2.0, 30.0),
        "narrow_alpha": (iapf_hz - 1.0, iapf_hz + 1.0),
        "relax_alpha": (iapf_hz - 1.0, iapf_hz + 1.0),
    }


def brainbeat_from_psd(freqs: np.ndarray, fz_psd: np.ndarray, pz_psd: np.ndarray,
                       iapf_hz: float, *, epsilon: float = 1e-20) -> dict[str, float] | None:
    """ANT window BrainBeat: relative Fz theta divided by relative Pz alpha."""
    bands = dynamic_bands(iapf_hz)
    fz_total = band_power(freqs, fz_psd, 1.0, 30.0) + epsilon
    pz_total = band_power(freqs, pz_psd, 1.0, 30.0) + epsilon
    fz_theta = band_power(freqs, fz_psd, *bands["theta"]) / fz_total
    pz_alpha = band_power(freqs, pz_psd, *bands["alpha"]) / pz_total
    value = brainbeat_ratio(fz_theta, pz_alpha)
    if value is None:
        return None
    return {"value": value, "fz_relative_theta": fz_theta, "pz_relative_alpha": pz_alpha}


def residual_spectrum(freqs: np.ndarray, psd: np.ndarray, offset: float,
                      exponent: float) -> np.ndarray:
    """ANT's clipped PSD minus fixed aperiodic 10**(offset-exponent*log10(f))."""
    frequencies = np.asarray(freqs, dtype=np.float64)
    power = np.asarray(psd, dtype=np.float64)
    if frequencies.ndim != 1 or power.ndim != 1 or frequencies.shape != power.shape:
        raise ValueError("residual spectrum expects matching vectors")
    if not np.isfinite(frequencies).all() or not np.isfinite(power).all() or np.any(power < 0):
        raise ValueError("residual spectrum inputs must be finite and non-negative")
    baseline = np.zeros_like(power)
    positive = frequencies > 0
    baseline[positive] = np.power(10.0, offset - exponent * np.log10(frequencies[positive]))
    return np.clip(power - baseline, 0.0, None)


def brainbeat_flat_from_psd(freqs: np.ndarray, fz_psd: np.ndarray, pz_psd: np.ndarray,
                            iapf_hz: float,
                            fz_fit: dict[str, object], pz_fit: dict[str, object]) -> float | None:
    bands = dynamic_bands(iapf_hz)
    fz_residual = residual_spectrum(freqs, fz_psd, fz_fit["aperiodic_offset"], fz_fit["aperiodic_exponent"])
    pz_residual = residual_spectrum(freqs, pz_psd, pz_fit["aperiodic_offset"], pz_fit["aperiodic_exponent"])
    fz_total = band_power(freqs, fz_residual, 1.0, 30.0)
    pz_total = band_power(freqs, pz_residual, 1.0, 30.0)
    if fz_total <= 0 or pz_total <= 0:
        return None
    return brainbeat_ratio(
        band_power(freqs, fz_residual, *bands["theta"]) / fz_total,
        band_power(freqs, pz_residual, *bands["alpha"]) / pz_total,
    )


def fatigue_ratio(theta_power: float, beta_power: float) -> float | None:
    if not np.isfinite(theta_power) or not np.isfinite(beta_power) or theta_power <= 0 or beta_power <= 0:
        return None
    return float(theta_power / beta_power)


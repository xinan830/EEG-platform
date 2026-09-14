"""Backend-only parity evidence for incremental official algorithm migration."""

from __future__ import annotations

import numpy as np
from scipy import signal

from app.eeg_core.primitives.compositions import fixed_band_rbp
from app.eeg_core.primitives.types import ChannelMap, PSDSeries, QualityMask, TimeRange
from app.eeg_core.primitives.units import Unit
from app.eeg_core.spectral import band_power
from app.eeg_core.faa import compute_faa
from app.eeg_core.realtime_spectral import segment_brainbeat
from app.eeg_core.offline_metrics import estimate_iapf, metric_values
from app.eeg_core.spectral import SpectralEstimate


RBP_BANDS = (("delta", 1.0, 4.0), ("theta", 4.0, 8.0), ("alpha", 8.0, 13.0), ("beta", 13.0, 30.0))


def shadow_rbp(freqs: np.ndarray, psd: np.ndarray, channels: list[str], *, rtol: float = 1e-7, atol: float = 1e-9) -> dict[str, object]:
    """Compare legacy and primitive RBP; this function never changes official output."""
    values = np.asarray(psd, dtype=np.float64)
    if values.ndim != 2 or values.shape[0] != len(channels):
        raise ValueError("PSD must be ordered channels-by-frequency")
    series = PSDSeries(values, np.asarray(freqs, dtype=float), ChannelMap(tuple(channels)), TimeRange(0.0, 1.0), Unit.V2_PER_HZ, QualityMask())
    legacy_total = np.asarray(band_power(freqs, values, 1.0, 30.0), dtype=float)
    rows = []
    for name, low, high in RBP_BANDS:
        legacy = np.asarray(band_power(freqs, values, low, high), dtype=float) / legacy_total
        primitive = fixed_band_rbp(series, low, high).values
        absolute = np.abs(primitive - legacy)
        relative = absolute / np.maximum(np.maximum(np.abs(primitive), np.abs(legacy)), np.finfo(float).tiny)
        rows.append({"band": name, "legacy": legacy.tolist(), "primitive": primitive.tolist(), "max_absolute_error": float(absolute.max()), "max_relative_error": float(relative.max()), "passed": bool(np.allclose(primitive, legacy, rtol=rtol, atol=atol))})
    return {"algorithm": "rbp", "channels": list(channels), "rtol": rtol, "atol": atol, "bands": rows, "passed": all(row["passed"] for row in rows)}


def shadow_faa(f3: np.ndarray, f4: np.ndarray, sfreq: float, *, rtol: float = 1e-7, atol: float = 1e-9) -> dict[str, object]:
    """Compare legacy FAA with an independent paired-epoch Hann reference."""
    legacy = compute_faa(f3, f4, sfreq)
    epoch = int(round(2.0 * sfreq))
    step = epoch // 2
    pairs = []
    for start in range(0, min(len(f3), len(f4)) - epoch + 1, step):
        left, right = np.asarray(f3[start:start + epoch], dtype=float), np.asarray(f4[start:start + epoch], dtype=float)
        left, right = left - left.mean(), right - right.mean()
        if np.isfinite(left).all() and np.isfinite(right).all() and max(np.abs(left).max(), np.abs(right).max()) <= 150e-6:
            pairs.append((left, right))
    candidate = {"faa": None, "p_f3": None, "p_f4": None, "reason": "too_few_clean_epochs"}
    if len(pairs) >= 10:
        window = signal.windows.hann(epoch, sym=False)
        frequencies = np.fft.rfftfreq(epoch, 1.0 / sfreq)
        psds = []
        for side in (0, 1):
            transformed = np.fft.rfft(np.stack([pair[side] for pair in pairs]) * window, axis=1)
            density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
            density[:, 1:-1] *= 2.0
            psds.append(density.mean(axis=0))
        p3, p4 = float(band_power(frequencies, psds[0], 8.0, 13.0)), float(band_power(frequencies, psds[1], 8.0, 13.0))
        if p3 > 0 and p4 > 0:
            candidate = {"faa": float(np.log(p4) - np.log(p3)), "p_f3": p3, "p_f4": p4, "reason": ""}
    if legacy["faa"] is None or candidate["faa"] is None:
        passed = legacy["faa"] is None and candidate["faa"] is None and legacy["reason"] == candidate["reason"]
        maximum = None
    else:
        maximum = abs(float(legacy["faa"]) - float(candidate["faa"]))
        passed = bool(np.isclose(legacy["faa"], candidate["faa"], rtol=rtol, atol=atol))
    return {"algorithm": "faa", "legacy": legacy, "candidate": candidate, "rtol": rtol, "atol": atol, "max_absolute_error": maximum, "passed": passed}


def _inclusive_trapezoid(freqs: np.ndarray, values: np.ndarray, low: float, high: float) -> float:
    """Independent realtime band integral: include only legacy in-range bins."""
    mask = (freqs >= low) & (freqs <= high)
    if int(mask.sum()) < 2:
        return 0.0
    return float(np.trapezoid(values[mask], freqs[mask]))


def _brainbeat_candidate(freqs: np.ndarray, fz_psd: np.ndarray, pz_psd: np.ndarray, iapf: float, epsilon: float) -> float:
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = _inclusive_trapezoid(freqs, fz_psd, 1.0, 30.0) + epsilon
    total_pz = _inclusive_trapezoid(freqs, pz_psd, 1.0, 30.0) + epsilon
    theta_fz = _inclusive_trapezoid(freqs, fz_psd, theta_low, theta_high) / total_fz
    alpha_pz = _inclusive_trapezoid(freqs, pz_psd, alpha_low, alpha_high) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))


def shadow_brainbeat(
    freqs: np.ndarray,
    fz_psd: np.ndarray,
    pz_psd: np.ndarray,
    iapf: float,
    *,
    epsilon: float = 1e-20,
    rtol: float = 1e-7,
    atol: float = 1e-9,
) -> dict[str, object]:
    """Compare one realtime BrainBeat frame without applying the EMA state machine."""
    frequencies = np.asarray(freqs, dtype=float)
    fz_values = np.asarray(fz_psd, dtype=float)
    pz_values = np.asarray(pz_psd, dtype=float)
    legacy = float(segment_brainbeat(frequencies, fz_values, pz_values, float(iapf), epsilon))
    candidate = _brainbeat_candidate(frequencies, fz_values, pz_values, float(iapf), epsilon)
    absolute = abs(candidate - legacy)
    relative = absolute / max(abs(candidate), abs(legacy), np.finfo(float).tiny)
    return {
        "algorithm": "brainbeat",
        "scope": "single_realtime_frame_without_ema",
        "legacy": legacy,
        "candidate": candidate,
        "iapf_hz": float(iapf),
        "epsilon": epsilon,
        "rtol": rtol,
        "atol": atol,
        "max_absolute_error": absolute,
        "max_relative_error": relative,
        "passed": bool(np.isclose(candidate, legacy, rtol=rtol, atol=atol)),
    }


def shadow_brainbeat_ema(
    frame_values: list[float], *, warmup_epochs: int, ema_alpha: float, rtol: float = 1e-7, atol: float = 1e-9
) -> dict[str, object]:
    """Independent log-domain EMA reference for the stateful realtime output.

    ``frame_values`` are already per-frame BrainBeat ratios.  This deliberately
    validates only state semantics; filtering and Welch input are covered by
    ``shadow_brainbeat`` and stay outside the offline algorithm contract.
    """
    if warmup_epochs < 1 or not 0.0 <= ema_alpha <= 1.0:
        raise ValueError("invalid BrainBeat EMA configuration")
    legacy_state: float | None = None
    candidate_state: float | None = None
    warmup: list[float] = []
    rows: list[dict[str, object]] = []
    for index, raw in enumerate(frame_values):
        value = float(raw)
        if not np.isfinite(value) or value <= 0.0:
            rows.append({"frame": index, "legacy": None if legacy_state is None else float(np.exp(legacy_state)), "candidate": None if candidate_state is None else float(np.exp(candidate_state)), "accepted": False})
            continue
        log_value = float(np.log(value))
        if legacy_state is None:
            warmup.append(log_value)
            if len(warmup) >= warmup_epochs:
                legacy_state = float(np.mean(warmup))
                candidate_state = sum(warmup) / len(warmup)
        else:
            legacy_state = float(ema_alpha * log_value + (1.0 - ema_alpha) * legacy_state)
            candidate_state = ema_alpha * log_value + (1.0 - ema_alpha) * float(candidate_state)
        legacy_output = None if legacy_state is None else float(np.exp(legacy_state))
        candidate_output = None if candidate_state is None else float(np.exp(candidate_state))
        rows.append({"frame": index, "legacy": legacy_output, "candidate": candidate_output, "accepted": True})
    compared = [(row["legacy"], row["candidate"]) for row in rows if row["legacy"] is not None]
    errors = [abs(float(left) - float(right)) for left, right in compared]
    return {
        "algorithm": "brainbeat",
        "scope": "realtime_log_ema_state_only",
        "warmup_epochs": warmup_epochs,
        "ema_alpha": ema_alpha,
        "frames": rows,
        "max_absolute_error": max(errors) if errors else None,
        "passed": bool(compared) and all(np.isclose(float(left), float(right), rtol=rtol, atol=atol) for left, right in compared),
    }


def _interpolated_trapezoid(freqs: np.ndarray, values: np.ndarray, low: float, high: float) -> float:
    """Independent offline integration matching the frozen boundary contract."""
    if low >= high or low < freqs[0] or high > freqs[-1]:
        raise ValueError("requested band is outside the PSD frequency axis")
    interior = (freqs > low) & (freqs < high)
    points = np.concatenate(([low], freqs[interior], [high]))
    return float(np.trapezoid(np.interp(points, freqs, values), points))


def shadow_theta_beta(
    spectrum: SpectralEstimate, iapf: float, channels: tuple[str, str, str] = ("Fz", "Pz", "Oz"), *, rtol: float = 1e-7, atol: float = 1e-9
) -> dict[str, object]:
    """Compare the offline IAPF-relative Theta/Beta metric per ordered channel."""
    if spectrum.psd.shape[0] != 3 or len(channels) != 3:
        raise ValueError("Theta/Beta shadow requires exactly Fz, Pz, and posterior third channel PSDs")
    legacy = metric_values(spectrum, float(iapf))["fatigue"]
    if spectrum.gate_failed:
        candidate: dict[str, float] = {}
    else:
        theta_low, theta_high = max(4.0, float(iapf) - 6.0), float(iapf) - 2.0
        beta_low, beta_high = float(iapf) + 2.0, 30.0
        candidate = {}
        # ``metric_values`` has a historical canonical output schema Fz/Pz/Oz.
        # Keep that schema while separately preserving the actual source label
        # (for example O2 can be mapped to the third posterior input).
        for index, name in enumerate(("Fz", "Pz", "Oz")):
            theta = _interpolated_trapezoid(spectrum.freqs, spectrum.psd[index], theta_low, theta_high)
            beta = _interpolated_trapezoid(spectrum.freqs, spectrum.psd[index], beta_low, beta_high)
            if beta > 0.0:
                candidate[name] = float(theta / beta)
    legacy_values = [float(legacy[name]) for name in ("Fz", "Pz", "Oz") if name in legacy]
    candidate_values = [float(candidate[name]) for name in ("Fz", "Pz", "Oz") if name in candidate]
    comparable = list(legacy) == list(candidate)
    absolute = [abs(float(legacy[name]) - float(candidate[name])) for name in legacy if name in candidate]
    relative = [error / max(abs(float(legacy[name])), abs(float(candidate[name])), np.finfo(float).tiny) for error, name in zip(absolute, legacy) if name in candidate]
    return {
        "algorithm": "theta_beta",
        "legacy": legacy,
        "candidate": candidate,
        "source_channels": list(channels),
        "output_channels": ["Fz", "Pz", "Oz"],
        "iapf_hz": float(iapf),
        "rtol": rtol,
        "atol": atol,
        "max_absolute_error": max(absolute) if absolute else None,
        "max_relative_error": max(relative) if relative else None,
        "passed": comparable and all(np.isclose(float(legacy[name]), float(candidate[name]), rtol=rtol, atol=atol) for name in legacy),
        "legacy_values": legacy_values,
        "candidate_values": candidate_values,
    }


def _iapf_candidate(freqs: np.ndarray, psd: np.ndarray, gate_failed: str | None) -> dict[str, object]:
    """Independent implementation of the frozen IAPF fit, residual, Peak/COG rules."""
    if gate_failed or not len(freqs):
        return {"value": None, "source": None, "gate_failed": gate_failed or "low_quality", "model_r2": None, "model_error": None, "peak_hz": None, "cog": None}
    average_psd = np.asarray(psd, dtype=float).mean(axis=0)
    frequencies = np.asarray(freqs, dtype=float)
    fit_mask = (frequencies >= 3.0) & (frequencies <= 30.0) & ~((frequencies >= 7.0) & (frequencies <= 13.0))
    if int(fit_mask.sum()) < 2 or np.any(average_psd[fit_mask] <= 0.0) or not np.isfinite(average_psd[fit_mask]).all():
        return {"value": None, "source": None, "gate_failed": "invalid_aperiodic_fit", "model_r2": None, "model_error": None, "peak_hz": None, "cog": None}
    x, y = np.log10(frequencies[fit_mask]), np.log10(average_psd[fit_mask])
    design = np.column_stack((x, np.ones_like(x)))
    slope, intercept = np.linalg.lstsq(design, y, rcond=None)[0]
    fitted = intercept + slope * x
    residual_log = y - fitted
    error = float(np.mean(np.abs(residual_log)))
    total = float(np.sum((y - y.mean()) ** 2))
    r2 = float(1.0 - np.sum(residual_log ** 2) / total) if total > 0.0 else None
    alpha_mask = (frequencies >= 7.0) & (frequencies <= 13.0)
    alpha_frequencies, alpha_psd = frequencies[alpha_mask], average_psd[alpha_mask]
    if not len(alpha_frequencies):
        return {"value": None, "source": None, "gate_failed": "no_peak_no_cog", "model_r2": r2, "model_error": error, "peak_hz": None, "cog": None}
    baseline = np.power(10.0, intercept + slope * np.log10(alpha_frequencies))
    residual_power = np.maximum(alpha_psd - baseline, 0.0)
    residual_sum = float(residual_power.sum())
    cog = float(np.dot(alpha_frequencies, residual_power) / residual_sum) if residual_sum > 0.0 else None
    peak_index = int(np.argmax(residual_power))
    peak = float(alpha_frequencies[peak_index])
    prominence = float(residual_power[peak_index] / max(float(baseline[peak_index]), 1e-20))
    if prominence >= 0.20:
        return {"value": peak, "source": "peak", "gate_failed": None, "model_r2": r2, "model_error": error, "peak_hz": peak, "cog": cog}
    if cog is not None:
        return {"value": cog, "source": "cog", "gate_failed": None, "model_r2": r2, "model_error": error, "peak_hz": peak, "cog": cog}
    return {"value": None, "source": None, "gate_failed": "no_peak_no_cog", "model_r2": r2, "model_error": error, "peak_hz": peak, "cog": None}


def shadow_iapf(spectrum: SpectralEstimate, *, rtol: float = 1e-7, atol: float = 1e-9) -> dict[str, object]:
    """Compare legacy IAPF with an independent least-squares residual reference."""
    legacy_estimate = estimate_iapf(spectrum)
    legacy = {
        "value": legacy_estimate.value, "source": legacy_estimate.source, "gate_failed": legacy_estimate.gate_failed,
        "model_r2": legacy_estimate.model_r2, "model_error": legacy_estimate.model_error,
        "peak_hz": legacy_estimate.peak_hz, "cog": legacy_estimate.cog,
    }
    candidate = _iapf_candidate(spectrum.freqs, spectrum.psd, spectrum.gate_failed)
    comparable = legacy["value"] is not None and candidate["value"] is not None
    absolute = abs(float(legacy["value"]) - float(candidate["value"])) if comparable else None
    passed = (
        bool(np.isclose(float(legacy["value"]), float(candidate["value"]), rtol=rtol, atol=atol))
        and legacy["source"] == candidate["source"]
        and legacy["gate_failed"] == candidate["gate_failed"]
    ) if comparable else legacy["value"] is None and candidate["value"] is None and legacy["gate_failed"] == candidate["gate_failed"]
    return {"algorithm": "iapf", "legacy": legacy, "candidate": candidate, "rtol": rtol, "atol": atol, "max_absolute_error": absolute, "passed": passed}

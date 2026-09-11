from dataclasses import asdict, dataclass
from typing import Any

import numpy as np
from scipy import signal

from app.models.recording import ChannelMapping


IAPF_WINDOW_S = 30.0
IAPF_STEP_S = 5.0
ARTIFACT_UV = 150.0


@dataclass(frozen=True)
class IAPFAttempt:
    elapsed_s: float
    iapf: float | None
    source: str | None
    signal_quality: float
    calibrated: bool
    gate_failed: str | None
    model_r2: float | None
    model_error: float | None
    gaussian_cf: float | None
    cog: float | None


@dataclass(frozen=True)
class MetricPoint:
    elapsed_s: float
    relaxation: float
    spatial_distribution: float
    rhythm_stability: float | None
    brainbeat: float | None
    fatigue: dict[str, float]


@dataclass(frozen=True)
class AnalysisResult:
    duration_s: float
    locked_iapf: float | None
    iapf_attempts: list[IAPFAttempt]
    metrics: list[MetricPoint]
    events: list[dict[str, Any]]
    waveform: dict[str, list[float]]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def _band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> float:
    mask = (freqs >= low) & (freqs <= high)
    if int(mask.sum()) < 2:
        return 0.0
    return float(np.trapezoid(psd[mask], freqs[mask]))


def _quality_mask(data: np.ndarray, sfreq: float) -> np.ndarray:
    window = max(1, int(round(2.0 * sfreq)))
    bad = np.zeros(len(data), dtype=bool)
    threshold = ARTIFACT_UV * 1e-6
    for start in range(0, len(data) - window + 1, window):
        if np.max(np.abs(data[start:start + window])) > threshold:
            bad[start:start + window] = True
    return ~bad


def _clean_psd(data: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, float]:
    good = _quality_mask(data, sfreq)
    quality = float(good.mean()) if len(good) else 0.0
    nperseg = max(8, int(round(4.0 * sfreq)))
    noverlap = nperseg // 2
    valid = data[good]
    if quality < 0.75 or len(valid) < nperseg:
        return np.array([]), np.array([]), quality
    freqs, psd = signal.welch(
        valid,
        fs=sfreq,
        nperseg=nperseg,
        noverlap=noverlap,
        window="hann",
        axis=0,
    )
    if psd.ndim > 1:
        psd = psd.T
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    filtered_psd = psd[:, mask] if psd.ndim > 1 else psd[mask]
    return freqs[mask], np.maximum(filtered_psd, 1e-20), quality


def _estimate_iapf(data: np.ndarray, sfreq: float) -> tuple[float | None, str | None, float, str | None, float | None, float | None, float | None, float | None]:
    freqs, psd, quality = _clean_psd(data, sfreq)
    if not len(freqs):
        return None, None, quality, "low_quality", None, None, None, None
    if psd.ndim > 1:
        psd = psd.mean(axis=0)
    fit_mask = (freqs >= 3.0) & (freqs <= 30.0)
    log_f = np.log10(freqs[fit_mask])
    log_p = np.log10(psd[fit_mask])
    slope, intercept = np.polyfit(log_f, log_p, 1)
    fitted = intercept + slope * log_f
    residual = log_p - fitted
    error = float(np.mean(np.abs(residual)))
    total = float(np.sum((log_p - log_p.mean()) ** 2))
    r2 = float(1.0 - np.sum((log_p - fitted) ** 2) / total) if total > 0 else None
    alpha_mask = (freqs >= 7.0) & (freqs <= 13.0)
    alpha_freqs = freqs[alpha_mask]
    alpha_psd = psd[alpha_mask]
    if not len(alpha_freqs):
        return None, None, quality, "no_peak_no_cog", r2, error, None, None
    baseline = np.power(10.0, intercept + slope * np.log10(alpha_freqs))
    residual_power = np.clip(alpha_psd - baseline, 0.0, None)
    if residual_power.sum() > 0:
        cog = float(np.sum(alpha_freqs * residual_power) / residual_power.sum())
    else:
        cog = None
    peak_index = int(np.argmax(alpha_psd))
    peak = float(alpha_freqs[peak_index])
    prominence = float(alpha_psd[peak_index] / max(np.median(alpha_psd), 1e-20) - 1.0)
    if prominence >= 0.20:
        return peak, "peak", quality, None, r2, error, peak, cog
    if cog is not None:
        return cog, "cog", quality, None, r2, error, peak, cog
    return None, None, quality, "no_peak_no_cog", r2, error, peak, cog


def _metric_point(window: np.ndarray, sfreq: float, elapsed_s: float, iapf: float) -> MetricPoint:
    freqs, psds, _ = _clean_psd(window, sfreq)
    if not len(freqs):
        return MetricPoint(elapsed_s, 0.0, 0.0, None, None, {})
    channel_psd = {index: psd for index, psd in enumerate(psds)} if psds.ndim > 1 else {0: psds}
    # 调用方始终按 Fz、Pz、Oz 排列三列；Fz 仅参与脑负荷，后部两列参与冥想指标。
    fz = channel_psd[0]
    posterior = np.mean([channel_psd[1], channel_psd[2]], axis=0)
    total = _band_power(freqs, posterior, 1.0, 30.0)
    narrow_alpha = _band_power(freqs, posterior, iapf - 1.0, iapf + 1.0)
    alpha = _band_power(freqs, posterior, iapf - 2.0, iapf + 2.0)
    relaxation = narrow_alpha / total if total else 0.0
    spatial = alpha / total if total else 0.0
    theta = _band_power(freqs, fz, max(4.0, iapf - 6.0), iapf - 2.0)
    beta = _band_power(freqs, fz, iapf + 2.0, 30.0)
    brainbeat = float(np.log10((beta + 1e-20) / (theta + 1e-20)))
    fatigue = {
        name: _band_power(freqs, channel_psd[index], max(4.0, iapf - 6.0), iapf - 2.0)
        / max(_band_power(freqs, channel_psd[index], iapf + 2.0, 30.0), 1e-20)
        for index, name in enumerate(("Fz", "Pz", "Oz"))
    }
    return MetricPoint(elapsed_s, relaxation, spatial, None, brainbeat, fatigue)


def _downsample_waveform(data: np.ndarray, sfreq: float, channel_names: list[str], max_points: int = 4000) -> dict[str, list[float]]:
    step = max(1, int(np.ceil(len(data) / max_points)))
    sampled = data[::step]
    return {
        "elapsed_s": (np.arange(len(sampled)) * step / sfreq).round(6).tolist(),
        "channels": {name: (sampled[:, index] * 1e6).round(4).tolist() for index, name in enumerate(channel_names)},
    }


def analyze_recording(data: np.ndarray, sfreq: float, mapping: ChannelMapping, channel_names: list[str], events: list[dict[str, Any]]) -> AnalysisResult:
    values = np.asarray(data, dtype=float)
    if values.ndim != 2 or len(values) == 0:
        raise ValueError("脑电数据必须是非空二维数组")
    name_to_index = {name.upper(): index for index, name in enumerate(channel_names)}
    ordered_names = [mapping.fz, mapping.pz, mapping.oz]
    try:
        ordered = values[:, [name_to_index[name.upper()] for name in ordered_names]]
    except KeyError as exc:
        raise ValueError(f"缺少映射通道: {exc.args[0]}") from exc

    window_samples = int(round(IAPF_WINDOW_S * sfreq))
    step_samples = int(round(IAPF_STEP_S * sfreq))
    attempts: list[IAPFAttempt] = []
    candidates: list[float] = []
    locked_iapf: float | None = None
    last_iapf = 10.0
    for end in range(window_samples, len(ordered) + 1, step_samples):
        estimate, source, quality, failed, r2, error, peak, cog = _estimate_iapf(ordered[end - window_samples:end], sfreq)
        calibrated = estimate is not None
        if calibrated:
            last_iapf = estimate
            if locked_iapf is None:
                candidates.append(estimate)
                if len(candidates) >= 3:
                    locked_iapf = float(np.median(candidates))
        attempts.append(IAPFAttempt(end / sfreq, estimate, source, quality, calibrated, failed, r2, error, peak, cog))

    metric_window = int(round(4.0 * sfreq))
    metrics = [
        _metric_point(ordered[end - metric_window:end], sfreq, end / sfreq, locked_iapf or last_iapf)
        for end in range(int(round(7.0 * sfreq)), len(ordered) + 1, int(round(sfreq)))
    ]
    return AnalysisResult(
        duration_s=float(len(values) / sfreq),
        locked_iapf=locked_iapf,
        iapf_attempts=attempts,
        metrics=metrics,
        events=list(events),
        waveform=_downsample_waveform(values, sfreq, channel_names),
    )

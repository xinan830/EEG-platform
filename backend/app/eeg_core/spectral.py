"""可独立测试的离线预处理、Welch PSD 和频段积分。"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
from scipy import signal

from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT
from app.eeg_core.quality import evaluate_spectral_window


@dataclass(frozen=True)
class SpectralEstimate:
    freqs: np.ndarray
    psd: np.ndarray
    signal_quality: float
    clean_epochs: int
    total_epochs: int
    gate_failed: str | None
    rejected_reasons: tuple[str, ...] = ()
    evidence: dict[str, Any] = field(default_factory=dict)


def preprocess_offline(data: np.ndarray, sfreq: float) -> np.ndarray:
    """整段零相位 SOS 带通；输入/输出均为 `(samples, channels)`、单位 V。"""
    values = np.asarray(data, dtype=float)
    if values.ndim != 2 or not len(values):
        raise ValueError("脑电数据必须是非空二维数组")
    low, high = (float(value) for value in ANALYSIS_CONTRACT["bandpass_hz"])
    if high >= float(sfreq) / 2:
        raise ValueError(f"分析高切必须低于奈奎斯特频率（{float(sfreq) / 2:g}Hz）")
    sos = signal.butter(
        int(ANALYSIS_CONTRACT["bandpass_prototype_order"]),
        [low, high], btype="bandpass", fs=float(sfreq), output="sos",
    )
    try:
        return signal.sosfiltfilt(sos, values, axis=0)
    except ValueError as exc:
        raise ValueError("记录太短，无法完成离线零相位滤波") from exc


def estimate_welch_psd(data: np.ndarray, sfreq: float) -> SpectralEstimate:
    """在分析窗口内用重叠 segment 计算 PSD，只平均 clean segment。"""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
    overlap = float(ANALYSIS_CONTRACT["welch_segment_overlap"])
    step = max(1, int(round(segment_samples * (1.0 - overlap))))
    bounds = range(0, max(0, len(values) - segment_samples + 1), step)
    segments = [values[start:start + segment_samples] for start in bounds]
    checks = [evaluate_spectral_window(item, segment_samples) for item in segments]
    clean = [item for item, check in zip(segments, checks) if check.status == "clean"]
    rejected_reasons = tuple(dict.fromkeys(
        reason for check in checks for reason in check.reasons
    ))
    if not segments:
        rejected_reasons = ("missing_samples",)
    quality = len(clean) / len(segments) if segments else 0.0
    minimum = float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"])
    if not segments or not clean or quality < minimum:
        return SpectralEstimate(
            np.array([]), np.empty((values.shape[1], 0)), quality,
            len(clean), len(segments), "low_quality", rejected_reasons,
        )
    spectra = []
    freqs = np.array([])
    for item in clean:
        freqs, segment_psd = signal.welch(
            item, fs=sfreq, window=str(ANALYSIS_CONTRACT["welch_window"]),
            nperseg=segment_samples, noverlap=0, detrend="constant",
            scaling=str(ANALYSIS_CONTRACT["welch_scaling"]), axis=0,
        )
        spectra.append(segment_psd.T)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    averaged = np.mean(np.stack(spectra), axis=0)[:, mask]
    return SpectralEstimate(
        freqs[mask], np.maximum(averaged, 1e-20), quality,
        len(clean), len(segments), None, rejected_reasons,
    )


def band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> np.ndarray | float:
    """用线性插值补齐边界后，对连续频段做梯形积分。"""
    x = np.asarray(freqs, dtype=float)
    values = np.asarray(psd, dtype=float)
    if x.ndim != 1 or values.shape[-1] != len(x) or len(x) < 2:
        raise ValueError("PSD 与频率轴形状不匹配")
    if not np.all(np.diff(x) > 0) or low >= high or low < x[0] or high > x[-1]:
        raise ValueError("频段边界无效或超出频率轴")
    interior = (x > low) & (x < high)
    integration_freqs = np.concatenate(([low], x[interior], [high]))
    flat = values.reshape((-1, len(x)))
    integration_values = np.vstack([
        np.interp(integration_freqs, x, row) for row in flat
    ])
    result = np.trapezoid(integration_values, integration_freqs, axis=-1)
    result = result.reshape(values.shape[:-1])
    return float(result) if np.ndim(result) == 0 else result


def estimate_spectrogram(data: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Compute a 4 s Hann spectrogram with 1 s steps; output power is V²/Hz."""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(4.0 * sfreq))
    step_samples = int(round(1.0 * sfreq))
    if values.ndim != 2 or len(values) < segment_samples:
        raise ValueError("时频图至少需要 4 秒数据")
    frames = [values[start:start + segment_samples] for start in range(0, len(values) - segment_samples + 1, step_samples)]
    window = signal.get_window("hann", segment_samples)
    freqs = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    spectra = []
    for frame in frames:
        # Match scipy.signal.welch(..., detrend="constant") used by the
        # static PSD path: every 4 s frame is demeaned before Hann/FFT.
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        spectra.append(density.T)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    starts = np.arange(0, len(values) - segment_samples + 1, step_samples, dtype=float)
    # A time bin denotes the center of its 4-second analysis window.
    centers = (starts + segment_samples / 2.0) / sfreq
    return centers, freqs[mask], np.stack(spectra)[:, :, mask]


def estimate_spectrogram_with_quality(data: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[dict[str, object]]]:
    """Return spectrogram power plus one quality record for every time window.

    The time axis and FFT math are identical to :func:`estimate_spectrogram`.
    A window is bad when it contains a non-finite sample or exceeds the shared
    analysis artifact peak threshold. Bad windows keep their time position and
    are represented by NaN power so renderers can show a gap.
    """
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(4.0 * sfreq))
    step_samples = int(round(1.0 * sfreq))
    if values.ndim != 2 or len(values) < segment_samples:
        raise ValueError("时频图至少需要 4 秒数据")
    starts = np.arange(0, len(values) - segment_samples + 1, step_samples, dtype=int)
    window = signal.get_window("hann", segment_samples)
    freqs = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    spectra: list[np.ndarray] = []
    quality: list[dict[str, object]] = []
    for start in starts:
        frame = values[start:start + segment_samples]
        check = evaluate_spectral_window(frame, segment_samples)
        bad_reason = check.reasons[0] if check.reasons else None
        centered = np.nan_to_num(frame)
        centered = centered - np.mean(centered, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        power = density.T[:, mask]
        if bad_reason is not None:
            power[:] = np.nan
        spectra.append(power)
        center = (float(start) + segment_samples / 2.0) / sfreq
        quality.append({
            "center_s": center,
            "start_s": float(start) / sfreq,
            "end_s": (float(start) + segment_samples) / sfreq,
            "status": check.status,
            "reason": bad_reason,
            "reasons": list(check.reasons),
            "peak_uv": check.peak_uv,
        })
    centers = np.asarray([item["center_s"] for item in quality], dtype=float)
    return centers, freqs[mask], np.stack(spectra), quality

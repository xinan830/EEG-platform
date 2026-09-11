"""可独立测试的离线预处理、Welch PSD 和频段积分。"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from scipy import signal

from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT


@dataclass(frozen=True)
class SpectralEstimate:
    freqs: np.ndarray
    psd: np.ndarray
    signal_quality: float
    clean_epochs: int
    total_epochs: int
    gate_failed: str | None


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
    """成对剔除坏 epoch 后平均 PSD，禁止拼接不连续样本。"""
    values = np.asarray(data, dtype=float)
    epoch_samples = int(round(float(ANALYSIS_CONTRACT["welch_epoch_s"]) * sfreq))
    step = max(1, int(round(epoch_samples * (1.0 - float(ANALYSIS_CONTRACT["welch_epoch_overlap"])))))
    bounds = range(0, max(0, len(values) - epoch_samples + 1), step)
    epochs = [values[start:start + epoch_samples] for start in bounds]
    threshold_v = float(ANALYSIS_CONTRACT["artifact_peak_uv"]) * 1e-6
    clean = [epoch for epoch in epochs if np.isfinite(epoch).all() and np.max(np.abs(epoch)) <= threshold_v]
    quality = len(clean) / len(epochs) if epochs else 0.0
    minimum = float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"])
    if not epochs or not clean or quality < minimum:
        return SpectralEstimate(np.array([]), np.empty((values.shape[1], 0)), quality, len(clean), len(epochs), "low_quality")
    spectra = []
    freqs = np.array([])
    for epoch in clean:
        freqs, epoch_psd = signal.welch(
            epoch, fs=sfreq, window=str(ANALYSIS_CONTRACT["welch_window"]),
            nperseg=epoch_samples, noverlap=0, detrend="constant",
            scaling=str(ANALYSIS_CONTRACT["welch_scaling"]), axis=0,
        )
        spectra.append(epoch_psd.T)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    averaged = np.mean(np.stack(spectra), axis=0)[:, mask]
    return SpectralEstimate(freqs[mask], np.maximum(averaged, 1e-20), quality, len(clean), len(epochs), None)


def band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> np.ndarray | float:
    """对 PSD 最后一维做闭区间梯形积分，单位由 PSD 决定。"""
    mask = (freqs >= low) & (freqs <= high)
    if int(mask.sum()) < 2:
        result = np.zeros(np.asarray(psd).shape[:-1], dtype=float)
    else:
        result = np.trapezoid(np.asarray(psd)[..., mask], freqs[mask], axis=-1)
    return float(result) if np.ndim(result) == 0 else result

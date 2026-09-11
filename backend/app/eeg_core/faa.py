"""额叶 alpha 不对称（FAA）的共享纯函数。"""

from __future__ import annotations

import numpy as np
from scipy import signal

from app.eeg_core.spectral import band_power

ARTIFACT_THRESHOLD_UV = 150.0
FAA_CHANNELS = ("F3", "F4")
FAA_BAND = (8.0, 13.0)
FAA_DISCARD_S = 12.0
FAA_EPOCH_S = 2.0
FAA_EPOCH_OVERLAP = 0.5
FAA_MIN_CLEAN_EPOCHS = 10
FAA_PERIOD_CAP_S = 1800.0


def faa_epoch_bounds(n_samples: int, epoch_samples: int, step_samples: int) -> list[tuple[int, int]]:
    if epoch_samples <= 0 or step_samples <= 0 or n_samples < epoch_samples:
        return []
    return [(start, start + epoch_samples) for start in range(0, n_samples - epoch_samples + 1, step_samples)]


def compute_faa(f3, f4, sfreq, epoch_s=FAA_EPOCH_S, overlap=FAA_EPOCH_OVERLAP,
                artifact_uv=ARTIFACT_THRESHOLD_UV, band=FAA_BAND,
                min_clean_epochs=FAA_MIN_CLEAN_EPOCHS) -> dict[str, object]:
    """已滤波 F3/F4（V）经成对 epoch 剔除后计算 `ln(P_F4)-ln(P_F3)`。"""
    f3 = np.asarray(f3, dtype=float).ravel()
    f4 = np.asarray(f4, dtype=float).ravel()
    count = int(min(len(f3), len(f4)))
    epoch_samples = int(round(float(sfreq) * float(epoch_s)))
    step = max(1, int(round(epoch_samples * (1.0 - float(overlap)))))
    bounds = faa_epoch_bounds(count, epoch_samples, step)
    report: dict[str, object] = {
        "faa": None, "p_f3": None, "p_f4": None,
        "total_epochs": len(bounds), "clean_epochs": 0, "clean_ratio": 0.0,
        "epoch_s": float(epoch_s), "artifact_uv": float(artifact_uv),
        "band": [float(band[0]), float(band[1])], "reason": "",
    }
    if not bounds:
        report["reason"] = "too_short"
        return report
    threshold = float(artifact_uv) * 1e-6
    clean_f3, clean_f4 = [], []
    for start, stop in bounds:
        left = f3[start:stop] - np.mean(f3[start:stop])
        right = f4[start:stop] - np.mean(f4[start:stop])
        if not np.isfinite(left).all() or not np.isfinite(right).all():
            continue
        if np.max(np.abs(left)) > threshold or np.max(np.abs(right)) > threshold:
            continue
        clean_f3.append(left)
        clean_f4.append(right)
    report["clean_epochs"] = len(clean_f3)
    report["clean_ratio"] = len(clean_f3) / len(bounds)
    if len(clean_f3) < int(min_clean_epochs):
        report["reason"] = "too_few_clean_epochs"
        return report
    window = signal.get_window("hann", epoch_samples)
    scale = 1.0 / (float(sfreq) * float(np.sum(window ** 2)))
    freqs = np.fft.rfftfreq(epoch_samples, d=1.0 / float(sfreq))

    def mean_psd(epochs: list[np.ndarray]) -> np.ndarray:
        spectrum = np.fft.rfft(np.vstack(epochs) * window, axis=-1)
        density = np.abs(spectrum) ** 2 * scale
        density[:, 1:-1 if epoch_samples % 2 == 0 else None] *= 2.0
        return density.mean(axis=0)

    p_f3 = float(band_power(freqs, mean_psd(clean_f3), *band))
    p_f4 = float(band_power(freqs, mean_psd(clean_f4), *band))
    if p_f3 <= 0.0 or p_f4 <= 0.0:
        report["reason"] = "band_truncated"
        return report
    faa = float(np.log(p_f4) - np.log(p_f3))
    if not np.isfinite(faa):
        report["reason"] = "non_finite"
        return report
    report.update({"faa": faa, "p_f3": p_f3, "p_f4": p_f4})
    return report

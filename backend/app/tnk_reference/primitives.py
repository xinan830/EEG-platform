"""Independent numeric helpers for TNK reference formulas; inputs use V."""

from __future__ import annotations

import numpy as np
from scipy import signal


def band_power(freqs: np.ndarray, psd: np.ndarray, low_hz: float, high_hz: float) -> float:
    """ANT-style trapezoid over included grid points; fewer than two -> 0."""
    f = np.asarray(freqs, dtype=np.float64)
    p = np.asarray(psd, dtype=np.float64)
    if f.ndim != 1 or p.ndim != 1 or f.shape != p.shape:
        raise ValueError("frequency and PSD arrays must be equal-length vectors")
    if not np.isfinite(f).all() or not np.isfinite(p).all():
        raise ValueError("frequency and PSD arrays must be finite")
    if not np.isfinite(low_hz) or not np.isfinite(high_hz) or low_hz > high_hz:
        raise ValueError("band bounds must be finite and ordered")
    mask = (f >= low_hz) & (f <= high_hz)
    if np.count_nonzero(mask) < 2:
        return 0.0
    return float(np.trapezoid(p[mask], f[mask]))


def welch_2s(values_v: np.ndarray, sfreq_hz: float) -> tuple[np.ndarray, np.ndarray]:
    """Independent 2 s periodic-Hann Welch, 50% overlap, density scaling."""
    values = np.asarray(values_v, dtype=np.float64)
    if values.ndim == 1:
        values = values[:, None]
    if values.ndim != 2 or values.shape[0] == 0 or not np.isfinite(values).all():
        raise ValueError("TNK reference Welch requires finite samples-by-channels input")
    if not np.isfinite(sfreq_hz) or sfreq_hz <= 0:
        raise ValueError("TNK reference sampling rate must be positive")
    nperseg = min(int(sfreq_hz * 2), values.shape[0])
    if nperseg < 2:
        raise ValueError("TNK reference Welch requires at least two samples")
    freqs, psd = signal.welch(
        values, fs=float(sfreq_hz), window="hann", nperseg=nperseg,
        noverlap=min(nperseg // 2, nperseg - 1), nfft=nperseg,
        detrend="constant", scaling="density", axis=0, average="mean",
    )
    return np.asarray(freqs), np.asarray(psd.T)


def faa_epoch_psd(values_v: np.ndarray, sfreq_hz: float, epoch_s: float = 2.0,
                  overlap: float = 0.5, artifact_uv: float = 150.0) -> dict[str, object]:
    """Paired-channel epoch FFT reference; partial tail is discarded."""
    values = np.asarray(values_v, dtype=np.float64)
    if values.ndim != 2 or values.shape[1] != 2:
        raise ValueError("FAA reference expects samples-by-two paired channels")
    if not np.isfinite(sfreq_hz) or sfreq_hz <= 0 or not np.isfinite(values).all():
        raise ValueError("FAA reference input or sampling rate is invalid")
    if not np.isfinite(epoch_s) or epoch_s <= 0:
        raise ValueError("FAA epoch duration must be positive")
    if not np.isfinite(overlap) or not 0 <= overlap < 1:
        raise ValueError("FAA overlap must be in [0, 1)")
    if not np.isfinite(artifact_uv) or artifact_uv <= 0:
        raise ValueError("FAA artifact threshold must be positive")
    n = int(round(sfreq_hz * epoch_s))
    step = max(1, int(round(n * (1.0 - overlap))))
    starts = list(range(0, len(values) - n + 1, step)) if n > 0 else []
    freqs = np.fft.rfftfreq(n, d=1.0 / sfreq_hz) if n > 0 else np.empty(0)
    if not starts:
        return {"frequencies_hz": freqs, "psd": np.empty((2, 0)), "total_epochs": 0,
                "clean_epochs": 0, "reason": "too_short"}
    window = signal.get_window("hann", n)
    scale = 1.0 / (sfreq_hz * np.sum(window ** 2))
    accepted = []
    for start in starts:
        epoch = values[start:start + n]
        epoch = epoch - np.mean(epoch, axis=0, keepdims=True)
        if np.max(np.abs(epoch)) > artifact_uv * 1e-6:
            continue
        transformed = np.fft.rfft(epoch * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 * scale
        if n % 2 == 0:
            density[1:-1] *= 2.0
        else:
            density[1:] *= 2.0
        accepted.append(density.T)
    if not accepted:
        return {"frequencies_hz": freqs, "psd": np.empty((2, len(freqs))),
                "total_epochs": len(starts), "clean_epochs": 0, "reason": "no_clean_epochs"}
    return {"frequencies_hz": freqs, "psd": np.mean(np.stack(accepted), axis=0),
            "total_epochs": len(starts), "clean_epochs": len(accepted), "reason": None}


"""Offline preprocessing, Welch PSD, band integration, and spectrograms."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
from scipy import signal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.quality.spectral import evaluate_spectral_window
from app.scientific.contracts.types import TransformPaddingEvidence


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


def _gap_summary(checks: list[Any], values: np.ndarray | None = None, expected_samples: int | None = None) -> dict[str, Any]:
    if values is not None and values.ndim == 2:
        finite = np.isfinite(values).all(axis=1)
        non_finite_samples = int(np.count_nonzero(~finite))
        missing_samples = max((expected_samples or len(values)) - len(values), 0)
    else:
        missing_samples = int(sum(check.gap.missing_samples for check in checks))
        non_finite_samples = int(sum(check.gap.non_finite_samples for check in checks))
    return {
        "detected": any(check.gap.detected for check in checks),
        "policy": "reject",
        "missing_samples": missing_samples,
        "non_finite_samples": non_finite_samples,
        "imputed": False,
    }


def _spectral_evidence(checks: list[Any], *, transform_padding: TransformPaddingEvidence, values: np.ndarray | None = None, expected_samples: int | None = None) -> dict[str, Any]:
    return {
        "schema_version": "spectral-window-evidence-v1",
        "gap": _gap_summary(checks, values, expected_samples),
        "transform_padding": transform_padding.as_dict(),
    }


def preprocess_offline(data: np.ndarray, sfreq: float) -> np.ndarray:
    """Apply the declared zero-phase SOS bandpass to V-valued samples."""
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
    """Compute overlapping Welch segments and average clean segments only."""
    values = np.asarray(data, dtype=float)
    segment_samples = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
    overlap = float(ANALYSIS_CONTRACT["welch_segment_overlap"])
    step = max(1, int(round(segment_samples * (1.0 - overlap))))
    bounds = range(0, max(0, len(values) - segment_samples + 1), step)
    segments = [values[start:start + segment_samples] for start in bounds]
    checks = [evaluate_spectral_window(item, segment_samples) for item in segments]
    clean = [item for item, check in zip(segments, checks) if check.status == "clean"]
    rejected_reasons = tuple(dict.fromkeys(reason for check in checks for reason in check.reasons))
    if not segments:
        rejected_reasons = ("missing_samples",)
    quality = len(clean) / len(segments) if segments else 0.0
    minimum = float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"])
    if not segments or not clean or quality < minimum:
        return SpectralEstimate(
            np.array([]), np.empty((values.shape[1], 0)), quality,
            len(clean), len(segments), "low_quality", rejected_reasons,
            evidence=_spectral_evidence(checks, transform_padding=TransformPaddingEvidence(), values=values, expected_samples=segment_samples),
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
        evidence=_spectral_evidence(checks, transform_padding=TransformPaddingEvidence(), values=values, expected_samples=segment_samples),
    )


def band_power(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> np.ndarray | float:
    """Integrate a continuous band after linearly interpolating its edges."""
    x = np.asarray(freqs, dtype=float)
    values = np.asarray(psd, dtype=float)
    if x.ndim != 1 or values.shape[-1] != len(x) or len(x) < 2:
        raise ValueError("PSD 与频率轴形状不匹配")
    if not np.all(np.diff(x) > 0) or low >= high or low < x[0] or high > x[-1]:
        raise ValueError("频段边界无效或超出频率轴")
    interior = (x > low) & (x < high)
    integration_freqs = np.concatenate(([low], x[interior], [high]))
    flat = values.reshape((-1, len(x)))
    integration_values = np.vstack([np.interp(integration_freqs, x, row) for row in flat])
    result = np.trapezoid(integration_values, integration_freqs, axis=-1)
    result = result.reshape(values.shape[:-1])
    return float(result) if np.ndim(result) == 0 else result


def estimate_spectrogram(data: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Compute a 4 s Hann spectrogram with 1 s steps; output is V²/Hz."""
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
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        spectra.append(density.T)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    starts = np.arange(0, len(values) - segment_samples + 1, step_samples, dtype=float)
    centers = (starts + segment_samples / 2.0) / sfreq
    return centers, freqs[mask], np.stack(spectra)[:, :, mask]


def estimate_spectrogram_with_quality(data: np.ndarray, sfreq: float) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[dict[str, object]]]:
    """Return spectrogram power and one quality record for every time window."""
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
        # A bad input window is unavailable; do not silently turn recording
        # gaps into zeros. Transform padding, if ever introduced, must be
        # reported separately from input-gap evidence.
        if check.status != "clean":
            power = np.full((values.shape[1], int(np.count_nonzero(mask))), np.nan, dtype=float)
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
                "gap": check.gap.as_dict(),
                "transform_padding": TransformPaddingEvidence().as_dict(),
            })
            continue
        centered = frame - np.mean(frame, axis=0, keepdims=True)
        centered = centered - np.mean(centered, axis=0, keepdims=True)
        transformed = np.fft.rfft(centered * window[:, None], axis=0)
        density = np.abs(transformed) ** 2 / (sfreq * np.sum(window ** 2))
        density[1:-1] *= 2.0
        power = density.T[:, mask]
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
            "gap": check.gap.as_dict(),
            "transform_padding": TransformPaddingEvidence().as_dict(),
        })
    centers = np.asarray([item["center_s"] for item in quality], dtype=float)
    return centers, freqs[mask], np.stack(spectra), quality
